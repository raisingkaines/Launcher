using System;
using System.Net.Http;
using System.Net.Mime;
using System.Threading.Tasks;
using System.Xml.Linq;

using Launcher.Handlers;
using Launcher.Models;

using NLog;

namespace Launcher.Helpers;

public static class HttpHelper
{
    private static readonly Logger _logger = LogManager.GetCurrentClassLogger();
    private static readonly HttpClient _httpClient = CreateHttpClient();

    public static string UserAgent => $"{App.GetText("Text.Title")} v{App.CurrentVersion}";

    public static HttpClient CreateHttpClient()
    {
        var httpClient = new HttpClient(new HttpLoggingHandler(new HttpClientHandler()
        {
            AllowAutoRedirect = true
        }));

        httpClient.Timeout = TimeSpan.FromSeconds(10);

        httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);

        return httpClient;
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

        try
        {
            var xmlDocument = XDocument.Load(contentStream);

            if (!int.TryParse(xmlDocument.Root?.Attribute("version")?.Value, out int version))
            {
                var error = "Failed to get server manifest, unknown version.";

                _logger.Error(error);

                return (ManifestResult.InvalidVersion, error, null);
            }

            if (version != ServerManifest.ManifestVersion)
            {
                var error = $"""
                             Server manifest is out of date.
                             Server Version: {version}
                             Launcher Version: {ServerManifest.ManifestVersion}
                             """;

                _logger.Error(error);

                return (ManifestResult.Outdated, error, null);
            }
        }
        catch (Exception ex)
        {
            var error = "Failed to get server manifest, unknown version.";

            _logger.Error(ex, error);

            return (ManifestResult.InvalidVersion, error, null);
        }

        contentStream.Position = 0;

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

            if (version != ClientManifest.ManifestVersion)
            {
                var error = $"""
                             Client manifest is out of date.
                             Server Version: {version}
                             Launcher Version: {ClientManifest.ManifestVersion}
                             """;

                _logger.Error(error);

                return (ManifestResult.Outdated, error, null);
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