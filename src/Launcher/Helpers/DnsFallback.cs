using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Net.Http.Headers;
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
    /// How a resolver expects to be asked. Both are standard; which one a given resolver
    /// speaks is simply a matter of what it implements.
    /// </summary>
    private enum Wire
    {
        /// <summary>The JSON API Cloudflare and Google offer.</summary>
        Json,

        /// <summary>RFC 8484, a DNS message over HTTP. Everyone supports this.</summary>
        Binary
    }

    /// <summary>
    /// Public resolvers, by IP, in the order to try them. All present certificates
    /// covering these addresses, so HTTPS to them validates without a name lookup.
    ///
    /// More than one on purpose: a provider that filters DNS often blocks the well known
    /// resolvers too, and it is rarely all of them. Quad9 answers only in the binary
    /// format, which is the reason for supporting both.
    /// </summary>
    private static readonly (string Name, string Url, Wire Format)[] Resolvers =
    [
        ("Cloudflare", "https://1.1.1.1/dns-query", Wire.Json),
        ("Google", "https://8.8.8.8/resolve", Wire.Json),
        ("Quad9", "https://9.9.9.9/dns-query", Wire.Binary),
        ("Quad9 secondary", "https://149.112.112.112/dns-query", Wire.Binary)
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

        foreach (var (name, url, format) in Resolvers)
        {
            try
            {
                var addresses = format is Wire.Json
                    ? await QueryJsonAsync(url, host, cancellationToken)
                    : await QueryBinaryAsync(url, host, cancellationToken);

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

    private static async Task<IPAddress[]> QueryJsonAsync(string resolverUrl, string host, CancellationToken cancellationToken)
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


    /// <summary>
    /// RFC 8484: the same query a normal resolver would get, posted over HTTPS.
    ///
    /// The message itself is small - a twelve byte header, then the name written as
    /// length-prefixed labels - and the reply repeats the question before its answers,
    /// so the question has to be stepped over before they can be read.
    /// </summary>
    private static async Task<IPAddress[]> QueryBinaryAsync(string resolverUrl, string host, CancellationToken cancellationToken)
    {
        var found = new List<IPAddress>();

        foreach (ushort type in new ushort[] { 1, 28 })
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, resolverUrl)
            {
                Content = new ByteArrayContent(BuildQuery(host, type))
            };

            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/dns-message");
            request.Headers.Accept.ParseAdd("application/dns-message");

            // Quad9 refuses anything older with a 505, so ask for HTTP/2 and let a
            // resolver that does not want it negotiate back down.
            request.Version = HttpVersion.Version20;
            request.VersionPolicy = HttpVersionPolicy.RequestVersionOrLower;

            using var response = await _client.SendAsync(request, cancellationToken);

            if (!response.IsSuccessStatusCode)
                continue;

            var reply = await response.Content.ReadAsByteArrayAsync(cancellationToken);

            ReadAnswers(reply, found);
        }

        return found.ToArray();
    }

    private static byte[] BuildQuery(string host, ushort type)
    {
        var message = new List<byte>(host.Length + 18)
        {
            0, 0,                   // id - the transport is already one request to one reply
            1, 0,                   // recursion desired
            0, 1,                   // one question
            0, 0, 0, 0, 0, 0        // no answer, authority or additional records
        };

        foreach (var label in host.Split('.', StringSplitOptions.RemoveEmptyEntries))
        {
            var bytes = System.Text.Encoding.ASCII.GetBytes(label);

            if (bytes.Length > 63)
                throw new FormatException($"'{label}' is too long for a DNS label.");

            message.Add((byte)bytes.Length);
            message.AddRange(bytes);
        }

        message.Add(0);                     // the root label ends the name
        message.Add((byte)(type >> 8));
        message.Add((byte)type);
        message.AddRange([0, 1]);           // class IN

        return message.ToArray();
    }

    private static void ReadAnswers(byte[] reply, List<IPAddress> found)
    {
        if (reply.Length < 12)
            return;

        var questions = (reply[4] << 8) | reply[5];
        var answers = (reply[6] << 8) | reply[7];
        var offset = 12;

        for (var i = 0; i < questions && offset < reply.Length; i++)
        {
            offset = SkipName(reply, offset) + 4;       // then its type and class
        }

        for (var i = 0; i < answers && offset < reply.Length; i++)
        {
            offset = SkipName(reply, offset);

            if (offset + 10 > reply.Length)
                return;

            var type = (reply[offset] << 8) | reply[offset + 1];
            var length = (reply[offset + 8] << 8) | reply[offset + 9];

            offset += 10;

            if (offset + length > reply.Length)
                return;

            // Anything else here is a CNAME on the way to the answer.
            if ((type is 1 && length is 4) || (type is 28 && length is 16))
                found.Add(new IPAddress(reply.AsSpan(offset, length)));

            offset += length;
        }
    }

    /// <summary>
    /// Steps over a name, which is either labels ending in a zero byte or a pointer back
    /// to one written earlier in the message.
    /// </summary>
    private static int SkipName(byte[] reply, int offset)
    {
        while (offset < reply.Length)
        {
            var length = reply[offset];

            if (length == 0)
                return offset + 1;

            // The top two bits set marks a pointer, and a pointer ends the name.
            if ((length & 0xC0) == 0xC0)
                return offset + 2;

            offset += length + 1;
        }

        return offset;
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
