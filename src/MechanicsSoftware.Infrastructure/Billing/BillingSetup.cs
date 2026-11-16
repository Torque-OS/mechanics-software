using System.Diagnostics.CodeAnalysis;
using MechanicsSoftware.Application.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MechanicsSoftware.Infrastructure.Billing;

[ExcludeFromCodeCoverage]
public static class BillingSetup
{
    public static IServiceCollection AddBillingClient(this IServiceCollection services, IConfiguration configuration)
    {
        var settings = BillingSettings.FromConfiguration(configuration);

        services.AddHttpContextAccessor();
        services.AddTransient<ForwardAuthorizationHandler>();

        services.AddHttpClient<IBillingClient, BillingClient>(client => client.BaseAddress = settings.BaseAddress)
            .AddHttpMessageHandler<ForwardAuthorizationHandler>()
            .AddStandardResilienceHandler(BillingResilience.Configure);

        return services;
    }
}
