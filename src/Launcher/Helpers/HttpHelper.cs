using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Mime;
using System.Net.Sockets;
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

    private static async ValueTask<Stream> HappyEyeballsConnectAsync(SocketsHttpConnectionContext context, CancellationToken cancellationToken)
    {
        var dnsEndPoint = context.DnsEndPoint;

        var addresses = await Dns.GetHostAddressesAsync(dnsEndPoint.Host, cancellationToken).ConfigureAwait(false);

        if (addresses.Length == 0)
        {
            throw new SocketException((int)SocketError.HostNotFound);
        }

        // Try IPv4 first so a broken IPv6 route cannot stall the request, but keep IPv6 as a fallback.
        var orderedAddresses = addresses
            .OrderBy(address => address.AddressFamily == AddressFamily.InterNetwork ? 0 : 1)
            .ToArray();

        if (orderedAddresses.Length == 1)
        {
            var socket = await ConnectToAddressAsync(orderedAddresses[0], dnsEndPoint.Port, cancellationToken).ConfigureAwait(false);

            return new NetworkStream(socket, ownsSocket: true);
        }

        using var attemptsCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        var pendingTasks = new List<Task<Socket>>(orderedAddresses.Length);

        for (var i = 0; i < orderedAddresses.Length; i++)
        {
            var address = orderedAddresses[i];
            var staggerDelay = _connectionAttemptDelay * i;

            pendingTasks.Add(ConnectWithDelayAsync(address, dnsEndPoint.Port, staggerDelay, attemptsCts.Token));
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

    public static async Task<(ManifestResult Result, string Error, ServerManifest? ServerManifest)> GetServerManifestAsync(string serverUrl)
    {
        var serverManifestUri = UriHelper.JoinUriPaths(serverUrl, ServerManifest.FileName.ToLower());

        var response = await _httpClient.GetAsync(serverManifestUri);

        if (!response.IsSuccessStatusCode)
        {
            var error = $"""
                         Failed to get server manifest.
                         Http Error: {response.ReasonPhrase}
                         """;

            _logger.Error(error);

            return (ManifestResult.HttpError, error, null);
        }

        if (response.Content.Headers.ContentType?.MediaType is not (MediaTypeNames.Text.Xml or MediaTypeNames.Application.Xml))
        {
            var error = $"""
                         Failed to get server manifest, invalid format.
                         Content Type: {response.Content.Headers.ContentType}
                         """;

            _logger.Error(error);

            return (ManifestResult.InvalidFormat, error, null);
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

                return (ManifestResult.InvalidVersion, error, null);
            }

            if (version > ServerManifest.ManifestVersion)
            {
                var error = $"""
                             Server manifest is unsupported.
                             Server Version: {version}
                             Launcher Version: {ServerManifest.ManifestVersion}
                             """;

                _logger.Error(error);

                return (ManifestResult.UnsupportedVersion, error, null);
            }
        }
        catch (Exception ex)
        {
            var error = "Failed to get server manifest, unknown version.";

            _logger.Error(ex, error);

            return (ManifestResult.InvalidVersion, error, null);
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

                return (ManifestResult.DeserializeError, error, null);
            }

            var serverManifest = ServerManifestV1.ToServerManifest(serverManifestV1);

            return (ManifestResult.Success, string.Empty, serverManifest);
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

                return (ManifestResult.DeserializeError, error, null);
            }

            return (ManifestResult.Success, string.Empty, serverManifest);
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