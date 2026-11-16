using MechanicsSoftware.API.Extensions;
using MechanicsSoftware.API.Middleware;
using MechanicsSoftware.Application;
using MechanicsSoftware.Infrastructure;
using MechanicsSoftware.Infrastructure.Logging;
using MechanicsSoftware.Infrastructure.Persistence;
using MechanicsSoftware.Infrastructure.Persistence.SQL;
using MechanicsSoftware.API.Metrics;
using MechanicsSoftware.Application.Abstractions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Authorization;
using Microsoft.EntityFrameworkCore;
using System.Diagnostics.CodeAnalysis;
using Prometheus;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource
        .AddService(
            serviceName: builder.Configuration["DD_SERVICE"] ?? builder.Environment.ApplicationName,
            serviceVersion: builder.Configuration["DD_VERSION"] ?? "unknown"))
    .WithTracing(tracing =>
    {
        tracing
            .AddAspNetCoreInstrumentation(options =>
            {
                options.RecordException = true;
                options.Filter = httpContext =>
                    !httpContext.Request.Path.StartsWithSegments("/health");
            })
            .AddHttpClientInstrumentation(options =>
            {
                options.RecordException = true;
            })
            // MassTransit's ActivitySource: publish/send/consume spans for the saga messages.
            .AddSource("MassTransit");

        // No options callback: letting AddOtlpExporter() read OTEL_EXPORTER_OTLP_ENDPOINT/
        // OTEL_EXPORTER_OTLP_PROTOCOL itself is what makes it append the per-signal path
        // (/v1/traces) to the endpoint. Setting options.Endpoint here ourselves would
        // overwrite that already-suffixed URI with the bare host, which the collector
        // 404s on — silently, since OTel's own export failures aren't logged by default.
        var otlpEndpoint = builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"];
        if (!string.IsNullOrWhiteSpace(otlpEndpoint))
        {
            tracing.AddOtlpExporter();
        }
    });

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerDocumentation();

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddSingleton<IServiceOrderMetrics, PrometheusServiceOrderMetrics>();
builder.Services.AddHostedService<ServiceOrderMetricsRefreshService>();
builder.Logging.AddJsonConsole(options => options.IncludeScopes = true);

builder.Services.AddControllers(options =>
{
    var policy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
    options.Filters.Add(new AuthorizeFilter(policy));
});

builder.Services.AddAuthorization();
builder.Services.AddHealthChecks();

var app = builder.Build();

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseMiddleware<RequestLoggingMiddleware>();
app.UseMiddleware<GatewayKeyMiddleware>();
app.UseHttpMetrics();

app.UseSwagger();
app.UseSwaggerUI(options =>
{
    options.SwaggerEndpoint("/swagger/v1/swagger.json", "Mechanics Software API v1");
    options.RoutePrefix = "swagger";
});

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();

    var seeder = scope.ServiceProvider.GetRequiredService<DatabaseSeeder>();
    await seeder.SeedAsync();
}

app.UseMiddleware<ExceptionHandlingMiddleware>();

app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();

app.MapHealthChecks("/health").AllowAnonymous();
app.MapMetrics().AllowAnonymous();
app.MapControllers();

app.Lifetime.ApplicationStarted.Register(() =>
{
    var url = (app.Urls.FirstOrDefault() ?? "http://localhost:8080")
        .Replace("[::]", "localhost")
        .Replace("0.0.0.0", "localhost");
    app.Logger.SwaggerUIReady($"{url}/swagger");
});

await app.RunAsync();

[ExcludeFromCodeCoverage]
public partial class Program
{
    protected Program() { }
}
