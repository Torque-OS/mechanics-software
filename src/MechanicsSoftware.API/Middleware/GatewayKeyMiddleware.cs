using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace MechanicsSoftware.API.Middleware;

/// <summary>
/// Rejects requests that did not come through the API Gateway.
/// The gateway injects <see cref="HeaderName"/> on every proxied request, so the
/// cluster load balancer stays unusable even though its DNS name is public.
/// Disabled when GATEWAY_KEY is not configured, which keeps local runs and tests untouched.
/// </summary>
public sealed partial class GatewayKeyMiddleware
{
    public const string HeaderName = "X-Gateway-Key";

    private readonly RequestDelegate _next;
    private readonly ILogger<GatewayKeyMiddleware> _logger;
    private readonly byte[]? _expectedKey;

    public GatewayKeyMiddleware(
        RequestDelegate next,
        ILogger<GatewayKeyMiddleware> logger,
        IConfiguration configuration)
    {
        _next = next;
        _logger = logger;

        var configuredKey = configuration["GATEWAY_KEY"];
        _expectedKey = string.IsNullOrEmpty(configuredKey)
            ? null
            : Encoding.UTF8.GetBytes(configuredKey);
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (_expectedKey is null || IsExempt(context.Request.Path))
        {
            await _next(context);
            return;
        }

        if (!IsAuthorized(context.Request.Headers[HeaderName].ToString()))
        {
            LogBypassAttempt(context.Request.Path);

            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            context.Response.ContentType = "application/json";

            var body = JsonSerializer.Serialize(
                new { error = "Requests must go through the API Gateway." });
            await context.Response.WriteAsync(body);
            return;
        }

        await _next(context);
    }

    // Kubernetes probes and load balancer health checks reach the pod directly,
    // so they never carry the header.
    private static bool IsExempt(PathString path) =>
        path.StartsWithSegments("/health", StringComparison.OrdinalIgnoreCase)
        || path.Equals("/metrics", StringComparison.OrdinalIgnoreCase);

    private bool IsAuthorized(string providedKey)
    {
        if (string.IsNullOrEmpty(providedKey))
            return false;

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(providedKey),
            _expectedKey);
    }

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Rejected request to {Path} — missing or invalid gateway key")]
    private partial void LogBypassAttempt(string path);
}
