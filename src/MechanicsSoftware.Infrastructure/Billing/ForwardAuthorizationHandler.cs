using Microsoft.AspNetCore.Http;
using Microsoft.Net.Http.Headers;

namespace MechanicsSoftware.Infrastructure.Billing;

public sealed class ForwardAuthorizationHandler(IHttpContextAccessor httpContextAccessor) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var authorization = httpContextAccessor.HttpContext?.Request.Headers[HeaderNames.Authorization].ToString();

        if (!string.IsNullOrWhiteSpace(authorization) && request.Headers.Authorization is null)
            request.Headers.TryAddWithoutValidation(HeaderNames.Authorization, authorization);

        return base.SendAsync(request, cancellationToken);
    }
}
