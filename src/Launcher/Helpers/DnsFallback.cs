using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

using NLog;

namespace Launcher.Helpers;

/// <summary>
/// Resolves a host over DNS-over-HTTPS when the system resolver will not.
///
/// Some ISPs filter or hijack DNS, and the symptom is specific and confusing: everything
/// else on the connection works, the server is perfectly healthy, and the launcher simply
/// cannot look up the address, so the server shows as offline. A VPN fixes it, which is a
/// lot to ask of someone who just wants to play.
///
/// The resolvers are addressed by IP, so reaching them needs no DNS of its own. Only the
/// address lookup moves - the connection is still made to the same host, with the same
/// certificate check and the same SNI, so this changes nothing about who is trusted.
/// </summary>
public static class DnsFallback
{
    private static readonly Logger _logger = LogManager.GetCurrentClassLogger();

    /// <summary>
    /// Public resolvers, by IP. Both present certificates covering these addresses, so
    /// HTTPS to them validates without a name lookup.
    /// </summary>
    private static readonly (string Name, string Url)[] Resolvers =
    [
        ("Cloudflare", "https://1.1.1.1/dns-query"),
        ("Google", "https://8.8.8.8/resolve")
    ];

    /// <summary>
    /// Its own client on purpose: the shared one routes through the connect callback that
    /// calls this, which would be a loop.
    /// </summary>
    private static readonly HttpClient _client = new(new SocketsHttpHandler())
    {
        Timeout = TimeSpan.FromSeconds(5)
    };

    private static readonly ConcurrentDictionary<string, (DateTime When, IPAddress[] Addresses)> _cache = new();

    /// <summary>Short: an ISP problem can clear, and a server can move.</summary>
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromMinutes(5);

    public static async Task<IPAddress[]> ResolveAsync(string host, CancellationToken cancellationToken = default)
    {
        if (_cache.TryGetValue(host, out var cached) && DateTime.UtcNow - cached.When < CacheLifetime)
            return cached.Addresses;

        foreach (var (name, url) in Resolvers)
        {
            try
            {
                var addresses = await QueryAsync(url, host, cancellationToken);

                if (addresses.Length > 0)
                {
                    _logger.Info("Resolved '{Host}' through {Resolver} after the system resolver failed.", host, name);

                    _cache[host] = (DateTime.UtcNow, addresses);

                    return addresses;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "{Resolver} could not resolve '{Host}'.", name, host);
            }
        }

        return [];
    }

    private static async Task<IPAddress[]> QueryAsync(string resolverUrl, string host, CancellationToken cancellationToken)
    {
        var found = new List<IPAddress>();

        // A first, then AAAA: the caller prefers IPv4 anyway, and a broken IPv6 route is
        // one of the things this is trying to get past.
        foreach (var type in new[] { "A", "AAAA" })
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Get, $"{resolverUrl}?name={Uri.EscapeDataString(host)}&type={type}");

            // Both resolvers answer the same JSON shape behind this header.
            request.Headers.Accept.ParseAdd("application/dns-json");

            using var response = await _client.SendAsync(request, cancellationToken);

            if (!response.IsSuccessStatusCode)
                continue;

            var answer = await response.Content.ReadFromJsonAsync<DnsResponse>(cancellationToken);

            foreach (var record in answer?.Answer ?? [])
            {
                // 1 is A, 28 is AAAA. Anything else here is a CNAME on the way.
                if (record.Type is not (1 or 28) || string.IsNullOrWhiteSpace(record.Data))
                    continue;

                if (IPAddress.TryParse(record.Data, out var address))
                    found.Add(address);
            }
        }

        return found.ToArray();
    }

    private sealed class DnsResponse
    {
        [JsonPropertyName("Status")]
        public int Status { get; set; }

        [JsonPropertyName("Answer")]
        public List<DnsRecord>? Answer { get; set; }
    }

    private sealed class DnsRecord
    {
        [JsonPropertyName("type")]
        public int Type { get; set; }

        [JsonPropertyName("data")]
        public string? Data { get; set; }
    }
}
