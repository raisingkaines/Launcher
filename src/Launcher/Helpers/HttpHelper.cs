using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Mime;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

using Launcher.Handlers;
using Launcher.Models;
using Launcher.ViewModels;

using NLog;

namespace Launcher.Helpers;

public static class HttpHelper
{
    private static readonly Logger _logger = LogManager.GetCurrentClassLogger();
    private static readonly HttpClient _httpClient = CreateHttpClient();
    private static readonly HttpClient _downloadHttpClient = CreateDownloadHttpClient();
    private static readonly TimeSpan _connectionAttemptDelay = TimeSpan.FromMilliseconds(150);

    public static string UserAgent => $"{App.GetText("Text.Title")} v{App.CurrentVersion}";

    public static HttpClient DownloadHttpClient => _downloadHttpClient;

    /// <summary>
    /// The shared client for small API calls. Building one per request leaks its handler
    /// and its sockets, which matters now that portraits are uploaded per character.
    /// Do not dispose it.
    /// </summary>
    public static HttpClient SharedHttpClient => _httpClient;

    public static HttpClient CreateHttpClient()
    {
        var httpClient = new HttpClient(new HttpLoggingHandler(new SocketsHttpHandler()
        {
            AllowAutoRedirect = true,
            ConnectCallback = HappyEyeballsConnectAsync
        }));

        httpClient.Timeout = TimeSpan.FromSeconds(10);

        httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);

        return httpClient;
    }

    private static HttpClient CreateDownloadHttpClient()
    {
        var httpClient = new HttpClient(new SocketsHttpHandler()
        {
            AllowAutoRedirect = true,
            ConnectCallback = HappyEyeballsConnectAsync,
            PooledConnectionLifetime = TimeSpan.FromMinutes(10),
            PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),
            MaxConnectionsPerServer = Settings.Instance.DownloadThreads,
        });

        // Downloader enforces its own per-block timeouts, so disable the
        // overall client timeout to avoid cutting off large files on slow connections.
        httpClient.Timeout = Timeout.InfiniteTimeSpan;

        httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);

        return httpClient;
    }

    /// <summary>
    /// Hosts whose system DNS answer has already proven wrong, so the public resolver is
    /// used directly from then on. Session only: a network problem can clear, and this
    /// should not outlive the launcher being open.
    /// </summary>
    private static readonly ConcurrentDictionary<string, bool> _distrustedHosts = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Where an answer came from, which decides whether it is worth doubting later.
    /// </summary>
    private readonly record struct Resolution(IPAddress[] Addresses, bool FromSystemResolver);

    /// <summary>
    /// Stops trusting the system resolver for a host for the rest of the session.
    /// </summary>
    private static bool DistrustSystemDns(string host)
    {
        if (!_distrustedHosts.TryAdd(host, true))
            return false;

        _logger.Warn("Using a public resolver for '{Host}' from now on; the system one is answering wrongly.", host);

        return true;
    }

    /// <summary>
    /// The system resolver first, falling back to DNS-over-HTTPS when it will not answer.
    ///
    /// Some ISPs filter or hijack DNS, which looks exactly like the server being down:
    /// the rest of the connection is fine and the lookup is the only thing that fails.
    /// Only the lookup moves - the connection, certificate check and SNI are unchanged.
    ///
    /// Worth knowing that a blocked lookup does not always fail. Plenty of ISPs answer it
    /// with an address of their own instead, so the lookup succeeds and the connection
    /// goes somewhere wrong - which is why the caller can come back and say it did.
    /// </summary>
    private static async Task<Resolution> ResolveAsync(string host, CancellationToken cancellationToken)
    {
        if (!_distrustedHosts.ContainsKey(host))
        {
            try
            {
                var addresses = await Dns.GetHostAddressesAsync(host, cancellationToken).ConfigureAwait(false);

                if (addresses.Length > 0)
                    return new Resolution(addresses, FromSystemResolver: true);

                _logger.Warn("The system resolver returned nothing for '{Host}'; trying DNS-over-HTTPS.", host);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "The system resolver failed for '{Host}'; trying DNS-over-HTTPS.", host);
            }
        }

        var secure = await DnsFallback.ResolveAsync(host, cancellationToken).ConfigureAwait(false);

        return new Resolution(secure, FromSystemResolver: false);
    }

    private static async ValueTask<Stream> HappyEyeballsConnectAsync(SocketsHttpConnectionContext context, CancellationToken cancellationToken)
    {
        var dnsEndPoint = context.DnsEndPoint;

        var resolution = await ResolveAsync(dnsEndPoint.Host, cancellationToken).ConfigureAwait(false);

        if (resolution.Addresses.Length == 0)
        {
            throw new SocketException((int)SocketError.HostNotFound);
        }

        try
        {
            return await ConnectToAnyAsync(resolution.Addresses, dnsEndPoint.Port, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (resolution.FromSystemResolver && !cancellationToken.IsCancellationRequested)
        {
            // Not one of the addresses the system gave us would take a connection. A
            // hijacked lookup pointed at something dead looks exactly like this, so ask a
            // public resolver before reporting the server down.
            var secure = await DnsFallback.ResolveAsync(dnsEndPoint.Host, cancellationToken).ConfigureAwait(false);
            var untried = secure.Except(resolution.Addresses).ToArray();

            if (untried.Length == 0)
                throw;

            _logger.Warn(ex, "Could not reach '{Host}' at the system resolver's addresses; trying {Count} from a public resolver.",
                dnsEndPoint.Host, untried.Length);

            DistrustSystemDns(dnsEndPoint.Host);

            return await ConnectToAnyAsync(untried, dnsEndPoint.Port, cancellationToken).ConfigureAwait(false);
        }
    }

    private static async ValueTask<Stream> ConnectToAnyAsync(IPAddress[] addresses, int port, CancellationToken cancellationToken)
    {
        // Try IPv4 first so a broken IPv6 route cannot stall the request, but keep IPv6 as a fallback.
        var orderedAddresses = addresses
            .OrderBy(address => address.AddressFamily == AddressFamily.InterNetwork ? 0 : 1)
            .ToArray();

        if (orderedAddresses.Length == 1)
        {
            var socket = await ConnectToAddressAsync(orderedAddresses[0], port, cancellationToken).ConfigureAwait(false);

            return new NetworkStream(socket, ownsSocket: true);
        }

        using var attemptsCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        var pendingTasks = new List<Task<Socket>>(orderedAddresses.Length);

        for (var i = 0; i < orderedAddresses.Length; i++)
        {
            var address = orderedAddresses[i];
            var staggerDelay = _connectionAttemptDelay * i;

            pendingTasks.Add(ConnectWithDelayAsync(address, port, staggerDelay, attemptsCts.Token));
        }

        Exception? lastException = null;

        while (pendingTasks.Count > 0)
        {
            var completed = await Task.WhenAny(pendingTasks).ConfigureAwait(false);
            pendingTasks.Remove(completed);

            if (completed.IsCompletedSuccessfully)
            {
                // Cancel the remaining attempts; their sockets are disposed in their own catch blocks.
                await attemptsCts.CancelAsync().ConfigureAwait(false);

                return new NetworkStream(completed.Result, ownsSocket: true);
            }

            lastException = completed.Exception?.GetBaseException() ?? lastException;
        }

        throw lastException ?? new SocketException((int)SocketError.HostUnreachable);
    }

    private static async Task<Socket> ConnectWithDelayAsync(IPAddress address, int port, TimeSpan delay, CancellationToken cancellationToken)
    {
        if (delay > TimeSpan.Zero)
        {
            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
        }

        return await ConnectToAddressAsync(address, port, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<Socket> ConnectToAddressAsync(IPAddress address, int port, CancellationToken cancellationToken)
    {
        var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp)
        {
            NoDelay = true
        };

        try
        {
            await socket.ConnectAsync(address, port, cancellationToken).ConfigureAwait(false);

            return socket;
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Turns a failed connection into something a player can act on. "Offline" covers a
    /// server that is genuinely down and a connection that is being interfered with, and
    /// those need completely different things from whoever reads it.
    /// </summary>
    private static string DescribeConnectionFailure(string requestUri, Exception ex)
    {
        // The caller builds the url as a string; show just the host, which is the part
        // that means anything to whoever is reading it.
        var host = Uri.TryCreate(requestUri, UriKind.Absolute, out var parsed) ? parsed.Host : requestUri;

        var socket = ex as SocketException ?? ex.InnerException as SocketException;

        if (socket is not null)
        {
            switch (socket.SocketErrorCode)
            {
                case SocketError.HostNotFound:
                case SocketError.NoData:
                case SocketError.TryAgain:
                    return $"Could not look up {host}. The address would not resolve, even through "
                         + "a public resolver, which usually means the network or its DNS is blocking it. "
                         + "A different DNS server, or a VPN, normally gets past this.";

                case SocketError.ConnectionRefused:
                    return $"{host} refused the connection. The server may be down.";

                case SocketError.TimedOut:
                case SocketError.NetworkUnreachable:
                case SocketError.HostUnreachable:
                    return $"Could not reach {host}. The connection timed out, which can mean the "
                         + "server is down or that something on the network is blocking it.";
            }
        }

        if (ex is AuthenticationException || ex.InnerException is AuthenticationException)
        {
            return $"The secure connection to {host} could not be established. Something on the "
                 + "network may be intercepting it.";
        }

        if (ex is TaskCanceledException or OperationCanceledException)
            return $"{host} did not answer in time.";

        return $"Could not reach {host}: {ex.Message}";
    }

    /// <summary>
    /// Fetches a server's manifest, and does not take a blocked lookup for an offline server.
    ///
    /// A provider that interferes with DNS does not necessarily break it. Answering with an
    /// address of its own is just as common, and then the lookup succeeds, the connection is
    /// made to the wrong place, and what comes back is a block page or a certificate for
    /// somebody else. Both arrive here looking like a broken server rather than a blocked
    /// one, so a reply of that shape is retried once through a public resolver.
    /// </summary>
    public static async Task<(ManifestResult Result, string Error, ServerManifest? ServerManifest)> GetServerManifestAsync(string serverUrl)
    {
        var attempt = await GetServerManifestOnceAsync(serverUrl);

        if (!attempt.Intercepted)
            return (attempt.Result, attempt.Error, attempt.ServerManifest);

        var host = Uri.TryCreate(serverUrl, UriKind.Absolute, out var parsed) ? parsed.Host : null;

        // Only worth a second attempt if the system resolver was still being trusted.
        if (host is null || !DistrustSystemDns(host))
            return (attempt.Result, attempt.Error, attempt.ServerManifest);

        _logger.Warn("'{Host}' answered in a way that suggests interference; retrying through a public resolver.", host);

        // The shared client keeps its connections, and one of them is already open to
        // whatever answered last time - so the connect callback, and with it the new
        // address, would never be consulted. A client of its own avoids that.
        using var fresh = CreateHttpClient();

        var retry = await GetServerManifestOnceAsync(serverUrl, fresh);

        if (retry.Result == ManifestResult.Success)
        {
            _logger.Info("'{Host}' worked once the system resolver was bypassed.", host);

            return (retry.Result, retry.Error, retry.ServerManifest);
        }

        // The retry did not help, so report the original failure rather than a confusing
        // second one about a different address.
        return (attempt.Result, attempt.Error, attempt.ServerManifest);
    }

    private static async Task<(ManifestResult Result, string Error, ServerManifest? ServerManifest, bool Intercepted)> GetServerManifestOnceAsync(string serverUrl, HttpClient? client = null)
    {
        var serverManifestUri = UriHelper.JoinUriPaths(serverUrl, ServerManifest.FileName.ToLower());

        HttpResponseMessage response;

        try
        {
            response = await (client ?? _httpClient).GetAsync(serverManifestUri);
        }
        catch (Exception ex)
        {
            // Reaching the server failed outright, which is not the same as the server
            // being down - and the difference is what someone needs in order to fix it.
            var error = DescribeConnectionFailure(serverManifestUri, ex);

            _logger.Error(ex, "Could not reach '{Uri}'.", serverManifestUri);

            // A certificate that does not match the host is the mark of something in
            // the middle of the connection rather than of a server being down.
            var intercepted = ex is AuthenticationException || ex.InnerException is AuthenticationException;

            return (ManifestResult.NetworkError, error, null, intercepted);
        }

        if (!response.IsSuccessStatusCode)
        {
            var error = $"""
                         Failed to get server manifest.
                         Http Error: {response.ReasonPhrase}
                         """;

            _logger.Error(error);

            return (ManifestResult.HttpError, error, null, false);
        }

        if (response.Content.Headers.ContentType?.MediaType is not (MediaTypeNames.Text.Xml or MediaTypeNames.Application.Xml))
        {
            // Something answering with a web page instead of a manifest usually means the
            // connection never arrived at the server at all.
            var error = response.Content.Headers.ContentType?.MediaType is MediaTypeNames.Text.Html
                ? $"{new Uri(serverManifestUri).Host} returned a web page instead of its manifest, which usually "
                  + "means something on the network answered in its place. A different DNS server, or a VPN, "
                  + "normally gets past this."
                : $"Failed to get server manifest, invalid format. Content Type: {response.Content.Headers.ContentType}";

            _logger.Error(error);

            return (ManifestResult.InvalidFormat, error, null, true);
        }

        using var contentStream = await response.Content.ReadAsStreamAsync();
        var version = 0;

        try
        {
            var xmlDocument = XDocument.Load(contentStream);

            if (!int.TryParse(xmlDocument.Root?.Attribute("version")?.Value, out version))
            {
                var error = "Failed to get server manifest, unknown version.";

                _logger.Error(error);

                return (ManifestResult.InvalidVersion, error, null, false);
            }

            if (version > ServerManifest.ManifestVersion)
            {
                var error = $"""
                             Server manifest is unsupported.
                             Server Version: {version}
                             Launcher Version: {ServerManifest.ManifestVersion}
                             """;

                _logger.Error(error);

                return (ManifestResult.UnsupportedVersion, error, null, false);
            }
        }
        catch (Exception ex)
        {
            var error = "Failed to get server manifest, unknown version.";

            _logger.Error(ex, error);

            return (ManifestResult.InvalidVersion, error, null, false);
        }

        contentStream.Position = 0;

        // Back-compat with v1 server manifest
        if (version == 1)
        {
            if (!XmlHelper.TryDeserialize<ServerManifestV1>(contentStream, ServerManifestV1.SchemaName, out var serverManifestV1, out var xmlError))
            {
                var error = $"""
                             Failed to get server manifest, invalid data.
                             Xml Error: {xmlError}
                             """;

                _logger.Error(error);

                return (ManifestResult.DeserializeError, error, null, false);
            }

            var serverManifest = ServerManifestV1.ToServerManifest(serverManifestV1);

            return (ManifestResult.Success, string.Empty, serverManifest, false);
        }

        // Current version
        {
            if (!XmlHelper.TryDeserialize<ServerManifest>(contentStream, ServerManifest.SchemaName, out var serverManifest, out var xmlError))
            {
                var error = $"""
                            Failed to get server manifest, invalid data.
                            Xml Error: {xmlError}
                            """;

                _logger.Error(error);

                return (ManifestResult.DeserializeError, error, null, false);
            }

            return (ManifestResult.Success, string.Empty, serverManifest, false);
        }
    }

    public static async Task<(ManifestResult Result, string Error, ClientManifest? ClientManifest)> GetClientManifestAsync(string serverUrl)
    {
        var clientManifestUri = UriHelper.JoinUriPaths(serverUrl, ClientManifest.FileName.ToLower());

        var response = await _httpClient.GetAsync(clientManifestUri);

        if (!response.IsSuccessStatusCode)
        {
            var error = $"""
                         Failed to get client manifest.
                         Http Error: {response.ReasonPhrase}
                         """;

            _logger.Error(error);

            return (ManifestResult.HttpError, error, null);
        }

        if (response.Content.Headers.ContentType?.MediaType is not (MediaTypeNames.Text.Xml or MediaTypeNames.Application.Xml))
        {
            var error = $"""
                         Failed to get client manifest, invalid format.
                         Content Type: {response.Content.Headers.ContentType}
                         """;

            _logger.Error(error);

            return (ManifestResult.InvalidFormat, error, null);
        }

        using var contentStream = await response.Content.ReadAsStreamAsync();

        try
        {
            var xmlDocument = XDocument.Load(contentStream);

            if (!int.TryParse(xmlDocument.Root?.Attribute("version")?.Value, out int version))
            {
                var error = "Failed to get client manifest, unknown version.";

                _logger.Error(error);

                return (ManifestResult.InvalidVersion, error, null);
            }

            if (version > ClientManifest.ManifestVersion)
            {
                var error = $"""
                             Client manifest is unsupported.
                             Server Version: {version}
                             Launcher Version: {ClientManifest.ManifestVersion}
                             """;

                _logger.Error(error);

                return (ManifestResult.UnsupportedVersion, error, null);
            }
        }
        catch (Exception ex)
        {
            var error = "Failed to get client manifest, unknown version.";

            _logger.Error(ex, error);

            return (ManifestResult.InvalidVersion, error, null);
        }

        contentStream.Position = 0;

        if (!XmlHelper.TryDeserialize<ClientManifest>(contentStream, ClientManifest.SchemaName, out var clientManifest, out var xmlError))
        {
            var error = $"""
                         Failed to get client manifest, invalid data.
                         Xml Error: {xmlError}
                         """;

            _logger.Error(error);

            return (ManifestResult.DeserializeError, error, null);
        }

        return (ManifestResult.Success, string.Empty, clientManifest);
    }
}