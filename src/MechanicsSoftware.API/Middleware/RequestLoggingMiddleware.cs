using System.Diagnostics;
using MechanicsSoftware.Application.Abstractions;

namespace MechanicsSoftware.API.Middleware;

public sealed class RequestLoggingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<RequestLoggingMiddleware> _logger;
    private readonly IServiceOrderMetrics? _metrics;

    public RequestLoggingMiddleware(
        RequestDelegate next,
        ILogger<RequestLoggingMiddleware> logger,
        IServiceOrderMetrics? metrics = null)
    {
        _next = next;
        _logger = logger;
        _metrics = metrics;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var start = Stopwatch.GetTimestamp();
        var correlationId = context.Request.Headers["X-Correlation-ID"].FirstOrDefault() ?? context.TraceIdentifier;

        context.TraceIdentifier = correlationId;

        using (_logger.BeginScope(new Dictionary<string, object>
        {
            ["correlation_id"] = correlationId,
            ["http_method"] = context.Request.Method,
            ["http_path"] = context.Request.Path.Value ?? string.Empty,
            ["http_host"] = context.Request.Host.Value ?? string.Empty,
            ["request_id"] = context.TraceIdentifier
        }))
        {
            try
            {
                await _next(context);
            }
            finally
            {
                var durationMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                var path = context.Request.Path.Value ?? "/";
                var method = context.Request.Method;
                var statusCode = context.Response.StatusCode;

                _logger.LogInformation(
                    "HTTP request completed: method={Method} path={Path} statusCode={StatusCode} durationMs={DurationMs}",
                    method,
                    path,
                    statusCode,
                    durationMs);

                _metrics?.RecordHttpLatency(durationMs, method, path, statusCode);

                if (statusCode >= 500)
                {
                    _logger.LogError(
                        "HTTP request failed: method={Method} path={Path} statusCode={StatusCode} durationMs={DurationMs}",
                        method,
                        path,
                        statusCode,
                        durationMs);

                    _metrics?.RecordHttpError(method, path, statusCode);
                }
            }
        }
    }
}
