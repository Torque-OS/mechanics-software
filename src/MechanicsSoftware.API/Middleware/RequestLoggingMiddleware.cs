using System.Diagnostics;
using MechanicsSoftware.Application.Abstractions;
using Microsoft.AspNetCore.Routing;

namespace MechanicsSoftware.API.Middleware;

public sealed class RequestLoggingMiddleware(
    RequestDelegate next,
    ILogger<RequestLoggingMiddleware> logger,
    IServiceOrderMetrics? metrics = null)
{
    private readonly RequestDelegate _next = next;
    private readonly ILogger<RequestLoggingMiddleware> _logger = logger;
    private readonly IServiceOrderMetrics? _metrics = metrics;

    public async Task InvokeAsync(HttpContext context)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            await _next(context);
        }
        finally
        {
            stopwatch.Stop();
            var durationMs = stopwatch.Elapsed.TotalMilliseconds;
            var path = context.Request.Path.Value ?? "/";
            var method = context.Request.Method;
            var statusCode = context.Response.StatusCode;
            var safePath = SanitizeForLog(path);
            var safeMethod = SanitizeForLog(method);
            
            var pathTemplate = (context.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText ?? path;

            _logger.LogInformation(
                "HTTP request completed: method={Method} path={Path} statusCode={StatusCode} durationMs={DurationMs}",
                safeMethod,
                safePath,
                statusCode,
                durationMs);

            _metrics?.RecordHttpLatency(durationMs, method, pathTemplate, statusCode);

            if (statusCode >= 500)
            {
                _logger.LogError(
                    "HTTP request failed: method={Method} path={Path} statusCode={StatusCode} durationMs={DurationMs}",
                    safeMethod,
                    safePath,
                    statusCode,
                    durationMs);

                _metrics?.RecordHttpError(method, pathTemplate, statusCode);
            }
        }
    }

    private static string SanitizeForLog(string value) =>
        value.Replace("\r", "\\r", StringComparison.Ordinal)
             .Replace("\n", "\\n", StringComparison.Ordinal);
}
