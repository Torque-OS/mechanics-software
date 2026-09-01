using FluentAssertions;
using MechanicsSoftware.API.Middleware;
using MechanicsSoftware.Application.Abstractions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace MechanicsSoftware.UnitTests.API;

public class RequestLoggingMiddlewareTests
{
    [Fact]
    public async Task InvokeAsync_AlwaysRecordsLatency()
    {
        var metrics = new Mock<IServiceOrderMetrics>();
        var middleware = new RequestLoggingMiddleware(
            context =>
            {
                context.Response.StatusCode = StatusCodes.Status204NoContent;
                return Task.CompletedTask;
            },
            NullLogger<RequestLoggingMiddleware>.Instance,
            metrics.Object);

        var context = BuildHttpContext("GET", "/api/service-orders/123/complete", "/api/service-orders/{id}/complete");

        await middleware.InvokeAsync(context);

        metrics.Verify(m => m.RecordHttpLatency(
                It.IsAny<double>(),
                "GET",
                "/api/service-orders/{id}/complete",
                StatusCodes.Status204NoContent),
            Times.Once);
        metrics.Verify(m => m.RecordHttpError(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task InvokeAsync_WhenStatusIs5xx_RecordsErrorMetric()
    {
        var metrics = new Mock<IServiceOrderMetrics>();
        var middleware = new RequestLoggingMiddleware(
            context =>
            {
                context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                return Task.CompletedTask;
            },
            NullLogger<RequestLoggingMiddleware>.Instance,
            metrics.Object);

        var context = BuildHttpContext("POST", "/api/service-orders/123/complete", "/api/service-orders/{id}/complete");

        await middleware.InvokeAsync(context);

        metrics.Verify(m => m.RecordHttpLatency(It.IsAny<double>(), "POST", "/api/service-orders/{id}/complete", 500), Times.Once);
        metrics.Verify(m => m.RecordHttpError("POST", "/api/service-orders/{id}/complete", 500), Times.Once);
    }

    [Fact]
    public async Task InvokeAsync_WithNullMetrics_DoesNotThrow()
    {
        var reachedNext = false;
        var middleware = new RequestLoggingMiddleware(
            _ =>
            {
                reachedNext = true;
                return Task.CompletedTask;
            },
            NullLogger<RequestLoggingMiddleware>.Instance);

        var context = BuildHttpContext("GET", "/health", "/health");

        var action = async () => await middleware.InvokeAsync(context);

        await action.Should().NotThrowAsync();
        reachedNext.Should().BeTrue();
    }

    private static DefaultHttpContext BuildHttpContext(string method, string path, string routeTemplate)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = method;
        context.Request.Path = path;
        context.SetEndpoint(new RouteEndpoint(
            _ => Task.CompletedTask,
            RoutePatternFactory.Parse(routeTemplate),
            0,
            EndpointMetadataCollection.Empty,
            "test"));
        return context;
    }
}
