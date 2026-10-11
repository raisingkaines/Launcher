using System;
using System.Collections.Concurrent;
using System.IO;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;

using Launcher.Models;

using NLog;

namespace Launcher.Helpers;

/// <summary>
/// The optional character select capability, described in
/// docs/character-select-protocol.md.
///
/// Support is probed rather than advertised: a server that has not implemented this
/// answers 404 and the player simply gets the in-game select screen, exactly as before.
/// Nothing here ever fails a login.
/// </summary>
public static class CharacterApi
{
    private static readonly Logger _logger = LogManager.GetCurrentClassLogger();

    /// <summary>
    /// How long to wait before deciding a server has not implemented this. The client is
    /// waiting on the answer, so an unsupported server must cost very little.
    /// </summary>
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Servers that gave a definite "no". Remembered for the session so one is not probed
    /// again on every login.
    ///
    /// Only definite answers go in here - a 404, or a reply that is not JSON. A timeout or
    /// a network blip must never latch the feature off, because the server may be fine on
    /// the next attempt.
    /// </summary>
    private static readonly ConcurrentDictionary<string, bool> _unsupported = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Forgets what was learned about a server, for when its address changes.</summary>
    public static void ForgetCapabilities(string webApiUrl) => _unsupported.TryRemove(webApiUrl, out _);

    /// <summary>
    /// Asks the server for the characters on the logged in account.
    /// Returns null when the server does not support this, or anything goes wrong.
    /// </summary>
    public static async Task<List<CharacterInfo>?> TryGetCharactersAsync(
        string webApiUrl, string sessionId, CancellationToken cancellationToken = default)
    {
        try
        {
            if (!TryBuildUri(webApiUrl, "characters", out var uri))
                return null;

            // Already known not to support this - do not make the player wait again.
            if (_unsupported.ContainsKey(webApiUrl))
                return null;

            using var request = new HttpRequestMessage(HttpMethod.Get, uri);

            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", sessionId);

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            timeout.CancelAfter(ProbeTimeout);

            using var response = await HttpHelper.SharedHttpClient
                .SendAsync(request, timeout.Token);

            if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed)
            {
                _logger.Debug("Server at '{Url}' does not support character select.", webApiUrl);

                _unsupported[webApiUrl] = true;

                return null;
            }

            if (!response.IsSuccessStatusCode)
            {
                _logger.Warn("Character list request to '{Url}' returned {Status}.", uri, response.StatusCode);

                return null;
            }

            // A 200 alone does not mean support. Servers commonly have a catch-all that
            // answers every unknown path - Server 1 returns 200 with an empty
            // octet-stream body - so require JSON before believing it.
            var mediaType = response.Content.Headers.ContentType?.MediaType;

            if (!string.Equals(mediaType, "application/json", StringComparison.OrdinalIgnoreCase))
            {
                _logger.Debug("Server at '{Url}' answered the character probe with '{MediaType}', treating as unsupported.",
                    webApiUrl, mediaType ?? "no content type");

                _unsupported[webApiUrl] = true;

                return null;
            }

            var result = await response.Content.ReadFromJsonAsync<CharacterListResponse>(cancellationToken);

            return result?.Characters;
        }
        catch (Exception ex)
        {
            _logger.Warn(ex, "Could not read the character list from '{Url}'.", webApiUrl);

            return null;
        }
    }

    /// <summary>
    /// Asks the server to resolve a chosen character and mint a launch ticket for it.
    /// Returns null when the server refuses, which means launching normally instead.
    /// </summary>
    public static async Task<CharacterLaunchResponse?> TryLaunchCharacterAsync(
        string webApiUrl, string sessionId, string guid, CancellationToken cancellationToken = default)
    {
        try
        {
            if (!TryBuildUri(webApiUrl, "characters/launch", out var uri))
                return null;

            using var request = new HttpRequestMessage(HttpMethod.Post, uri)
            {
                Content = JsonContent.Create(new { guid })
            };

            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", sessionId);

            using var response = await HttpHelper.SharedHttpClient
                .SendAsync(request, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                // 403/404 means the character is not on this account. The server is the
                // authority on that, so just fall back to a normal launch.
                _logger.Warn("Character launch request returned {Status} for guid {Guid}.", response.StatusCode, guid);

                return null;
            }

            var result = await response.Content.ReadFromJsonAsync<CharacterLaunchResponse>(cancellationToken);

            if (result is null || string.IsNullOrWhiteSpace(result.Ticket) || string.IsNullOrWhiteSpace(result.Guid))
            {
                _logger.Warn("Character launch response was missing a guid or ticket.");

                return null;
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.Warn(ex, "Could not request a character launch from '{Url}'.", webApiUrl);

            return null;
        }
    }

    /// <summary>
    /// Asks the server to send this account to the in-game character creator on the next
    /// login, and returns what it said. Null means the server would not, or could not,
    /// which just means launching normally and landing on the in-game select screen.
    ///
    /// The creator cannot be reached with a launch argument - the client decides between
    /// the creator and the select screen purely from what the login server reports - so
    /// this has to be asked for before the game starts, and the game then has to be
    /// launched through the normal login path rather than straight into a character.
    /// </summary>
    public static async Task<CharacterCreateResponse?> TryRequestCharacterCreationAsync(
        string webApiUrl, string sessionId, CancellationToken cancellationToken = default)
    {
        try
        {
            if (!TryBuildUri(webApiUrl, "characters/create", out var uri))
                return null;

            using var request = new HttpRequestMessage(HttpMethod.Post, uri);

            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", sessionId);

            using var response = await HttpHelper.SharedHttpClient
                .SendAsync(request, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                // 501 is a server that has not implemented this; anything else is a
                // refusal. Either way the launch still happens, just without the creator.
                _logger.Warn("Character create request returned {Status}.", response.StatusCode);

                return null;
            }

            var result = await response.Content.ReadFromJsonAsync<CharacterCreateResponse>(cancellationToken);

            if (result is null || !result.Ok)
            {
                _logger.Warn("Character create request was not acknowledged.");

                return null;
            }

            if (!result.HookInstalled)
                _logger.Warn("Server accepted the character create request but has no login patch installed.");

            return result;
        }
        catch (Exception ex)
        {
            _logger.Warn(ex, "Could not request character creation from '{Url}'.", webApiUrl);

            return null;
        }
    }

    /// <summary>
    /// Sends a headshot the game client captured to the server, for one character.
    /// Returns false when the server will not take it, which is never worth interrupting
    /// the player over.
    /// </summary>
    public static async Task<bool> TryUploadPortraitAsync(
        string webApiUrl, string sessionId, string guid, string pngPath,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (!TryBuildUri(webApiUrl, $"characters/{Uri.EscapeDataString(guid)}/portrait", out var uri))
                return false;

            byte[] png;

            try
            {
                png = await File.ReadAllBytesAsync(pngPath, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Could not read the captured headshot '{Path}'.", pngPath);

                return false;
            }

            if (png.Length == 0)
                return false;

            return await TryUploadPortraitBytesAsync(webApiUrl, sessionId, guid, png, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.Warn(ex, "Could not upload a portrait to '{Url}'.", webApiUrl);
            return false;
        }
    }

    public static async Task<bool> TryUploadPortraitBytesAsync(
        string webApiUrl, string sessionId, string guid, byte[] png,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (!TryBuildUri(webApiUrl, $"characters/{Uri.EscapeDataString(guid)}/portrait", out var uri))
                return false;

            using var content = new ByteArrayContent(png);
            content.Headers.ContentType = new MediaTypeHeaderValue("image/png");

            using var request = new HttpRequestMessage(HttpMethod.Post, uri) { Content = content };

            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", sessionId);

            using var response = await HttpHelper.SharedHttpClient
                .SendAsync(request, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                // 404 just means this server does not know the character, and 501/404 on the
                // route means it has not implemented portraits. Neither is an error here.
                _logger.Warn("Portrait upload returned {Status} for guid {Guid}.", response.StatusCode, guid);

                return false;
            }

            // Same trap as the character probe: a catch-all route can answer 200 to
            // anything. Recording that as sent would mean the portrait is never retried,
            // so require the JSON acknowledgement before believing it.
            var mediaType = response.Content.Headers.ContentType?.MediaType;

            if (!string.Equals(mediaType, "application/json", StringComparison.OrdinalIgnoreCase))
            {
                _logger.Debug("Portrait upload to '{Url}' answered '{MediaType}', treating as unsupported.",
                    webApiUrl, mediaType ?? "no content type");

                return false;
            }

            var ack = await response.Content.ReadFromJsonAsync<PortraitUploadResponse>(cancellationToken);

            return ack?.Ok == true;
        }
        catch (Exception ex)
        {
            _logger.Warn(ex, "Could not upload a portrait to '{Url}'.", webApiUrl);

            return false;
        }
    }

    /// <summary>
    /// Downloads a headshot. The URL comes from the server; the launcher never builds one.
    /// </summary>
    public static async Task<byte[]?> TryGetPortraitAsync(
        string? portraitUrl, string sessionId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(portraitUrl))
            return null;

        try
        {
            if (!Uri.TryCreate(portraitUrl, UriKind.Absolute, out var uri))
                return null;

            if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
                return null;

            // The portrait route is session protected, so one player cannot read another
            // player's headshot by guessing guids. Send the token or it answers 401.
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);

            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", sessionId);

            using var response = await HttpHelper.SharedHttpClient
                .SendAsync(request, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.Debug("Portrait request to '{Url}' returned {Status}.", uri, response.StatusCode);

                return null;
            }

            return await response.Content.ReadAsByteArrayAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.Debug(ex, "Could not download the portrait at '{Url}'.", portraitUrl);

            return null;
        }
    }

    private static bool TryBuildUri(string webApiUrl, string path, out Uri uri)
    {
        uri = null!;

        if (!Uri.TryCreate(webApiUrl, UriKind.Absolute, out var baseUri))
            return false;

        // A base without a trailing slash would drop its last segment.
        if (!baseUri.AbsolutePath.EndsWith('/'))
            baseUri = new Uri(baseUri + "/");

        uri = new Uri(baseUri, path);

        return true;
    }
}
