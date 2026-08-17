using System.Text;
using FluentAssertions;
using MechanicsSoftware.API.Middleware;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace MechanicsSoftware.UnitTests.API;

public class GatewayKeyMiddlewareTests
{
    private const string ConfiguredKey = "gateway-key-for-testing-only";

    private static IConfiguration BuildConfig(string? gatewayKey) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["GATEWAY_KEY"] = gatewayKey })
            .Build();

    private static async Task<(int StatusCode, bool ReachedNext, string Body)> InvokeAsync(
        string? gatewayKey,
        string path = "/api/customers",
        string? providedHeader = null)
    {
        var reachedNext = false;
        var middleware = new GatewayKeyMiddleware(
            _ =>
            {
                reachedNext = true;
                return Task.CompletedTask;
            },
            NullLogger<GatewayKeyMiddleware>.Instance,
            BuildConfig(gatewayKey));

        var context = new DefaultHttpContext();
        context.Request.Path = path;
        context.Response.Body = new MemoryStream();

        if (providedHeader is not null)
            context.Request.Headers[GatewayKeyMiddleware.HeaderName] = providedHeader;

        await middleware.InvokeAsync(context);

        context.Response.Body.Seek(0, SeekOrigin.Begin);
        var body = await new StreamReader(context.Response.Body, Encoding.UTF8).ReadToEndAsync();

        return (context.Response.StatusCode, reachedNext, body);
    }

    [Fact]
    public async Task NoKeyConfigured_LetsRequestThrough()
    {
        var result = await InvokeAsync(gatewayKey: null);

        result.ReachedNext.Should().BeTrue();
        result.StatusCode.Should().Be(StatusCodes.Status200OK);
    }

    [Fact]
    public async Task MatchingHeader_LetsRequestThrough()
    {
        var result = await InvokeAsync(ConfiguredKey, providedHeader: ConfiguredKey);

        result.ReachedNext.Should().BeTrue();
        result.StatusCode.Should().Be(StatusCodes.Status200OK);
    }

    [Fact]
    public async Task MissingHeader_IsRejected()
    {
        var result = await InvokeAsync(ConfiguredKey);

        result.ReachedNext.Should().BeFalse();
        result.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        result.Body.Should().Contain("API Gateway");
    }

    [Theory]
    [InlineData("wrong-key")]
    [InlineData("")]
    [InlineData("gateway-key-for-testing-only-longer")]
    public async Task InvalidHeader_IsRejected(string providedHeader)
    {
        var result = await InvokeAsync(ConfiguredKey, providedHeader: providedHeader);

        result.ReachedNext.Should().BeFalse();
        result.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
    }

    [Theory]
    [InlineData("/health")]
    [InlineData("/health/ready")]
    public async Task HealthProbes_BypassTheCheck(string path)
    {
        var result = await InvokeAsync(ConfiguredKey, path: path);

        result.ReachedNext.Should().BeTrue();
        result.StatusCode.Should().Be(StatusCodes.Status200OK);
    }
}
