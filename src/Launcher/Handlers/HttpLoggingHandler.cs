using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

using NLog;

namespace Launcher.Handlers;

public class HttpLoggingHandler : DelegatingHandler
{
    private readonly Logger _logger;

    public HttpLoggingHandler(HttpMessageHandler innerHandler) : base(innerHandler)
    {
        _logger = LogManager.GetCurrentClassLogger();
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        try
        {
            _logger.Info("HTTP Request: {Method} {RequestUri} | Content Headers: {ContentHeaders}",
                request.Method, request.RequestUri, request.Content?.Headers?.ToString() ?? "");

            var response = await base.SendAsync(request, cancellationToken);

            _logger.Info("HTTP Response: {StatusCode} {ReasonPhrase} for {RequestUri} | Content Headers: {ContentHeaders}",
                response.StatusCode, response.ReasonPhrase, request.RequestUri, request.Content?.Headers?.ToString() ?? "");

            return response;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "HTTP request failed");
            throw;
        }
    }
}