using Microsoft.Extensions.Configuration;

namespace MechanicsSoftware.Infrastructure.Billing;

public sealed record BillingSettings(Uri BaseAddress)
{
    public static BillingSettings FromConfiguration(IConfiguration configuration)
    {
        var url = configuration["BILLING_API_URL"];

        if (string.IsNullOrWhiteSpace(url))
            throw new InvalidOperationException(
                "Billing Service URL not configured. Set the 'BILLING_API_URL' environment variable.");

        if (!Uri.TryCreate(url.EndsWith('/') ? url : url + "/", UriKind.Absolute, out var baseAddress)
            || (baseAddress.Scheme != Uri.UriSchemeHttp && baseAddress.Scheme != Uri.UriSchemeHttps))
            throw new InvalidOperationException(
                $"Invalid value for 'BILLING_API_URL': '{url}'. Expected an absolute URL (e.g. http://billing:8080).");

        return new BillingSettings(baseAddress);
    }
}
