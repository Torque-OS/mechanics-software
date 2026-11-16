using FluentAssertions;
using MechanicsSoftware.Infrastructure.Billing;
using Microsoft.AspNetCore.Http;

namespace MechanicsSoftware.UnitTests.Infrastructure;

public class ForwardAuthorizationHandlerTests
{
    private static async Task<HttpRequestMessage> SendThroughAsync(HttpContext? context)
    {
        var inner = new CapturingHandler();
        var handler = new ForwardAuthorizationHandler(new HttpContextAccessor { HttpContext = context }) { InnerHandler = inner };

        using var invoker = new HttpMessageInvoker(handler);
        await invoker.SendAsync(new HttpRequestMessage(HttpMethod.Get, "http://billing.test/api/budgets"), CancellationToken.None);

        return inner.Request!;
    }

    [Fact]
    public async Task SendAsync_IncomingBearerToken_IsForwardedToBilling()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers.Authorization = "Bearer staff-token";

        var request = await SendThroughAsync(context);

        request.Headers.Authorization!.ToString().Should().Be("Bearer staff-token");
    }

    [Fact]
    public async Task SendAsync_NoIncomingToken_SendsNoAuthorization()
    {
        var request = await SendThroughAsync(new DefaultHttpContext());

        request.Headers.Authorization.Should().BeNull();
    }

    [Fact]
    public async Task SendAsync_OutsideARequest_SendsNoAuthorization()
    {
        var request = await SendThroughAsync(null);

        request.Headers.Authorization.Should().BeNull();
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK));
        }
    }
}
