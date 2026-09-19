using System;
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

            using var request = new HttpRequestMessage(HttpMethod.Get, uri);

            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", sessionId);

            using var response = await HttpHelper.CreateHttpClient()
                .SendAsync(request, cancellationToken);

            if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed)
            {
                _logger.Debug("Server at '{Url}' does not support character select.", webApiUrl);

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

            using var response = await HttpHelper.CreateHttpClient()
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
    /// Downloads a headshot. The URL comes from the server; the launcher never builds one.
    /// </summary>
    public static async Task<byte[]?> TryGetPortraitAsync(string? portraitUrl, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(portraitUrl))
            return null;

        try
        {
            if (!Uri.TryCreate(portraitUrl, UriKind.Absolute, out var uri))
                return null;

            if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
                return null;

            return await HttpHelper.CreateHttpClient().GetByteArrayAsync(uri, cancellationToken);
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
