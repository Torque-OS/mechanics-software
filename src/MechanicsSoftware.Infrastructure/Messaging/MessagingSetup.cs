using System.Diagnostics.CodeAnalysis;
using MassTransit;
using MechanicsSoftware.Application.Abstractions;
using MechanicsSoftware.Infrastructure.Messaging.Consumers;
using MechanicsSoftware.Infrastructure.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace MechanicsSoftware.Infrastructure.Messaging;

[ExcludeFromCodeCoverage]
public static class MessagingSetup
{
    public const string EndpointPrefix = "os-service";

    public static IServiceCollection AddMessaging(this IServiceCollection services, IConfiguration configuration)
    {
        var rabbitMq = RabbitMqSettings.FromConfiguration(configuration);

        services.AddScoped<IEventPublisher, MassTransitEventPublisher>();

        services.AddSingleton<SagaMetricsObserver>();
        services.AddConsumeObserver(provider => provider.GetRequiredService<SagaMetricsObserver>());

        services.AddMassTransit(bus =>
        {
            bus.SetEndpointNameFormatter(new KebabCaseEndpointNameFormatter(EndpointPrefix, false));

            bus.AddConsumer<BudgetGeneratedConsumer>();
            bus.AddConsumer<BudgetApprovedConsumer>();
            bus.AddConsumer<BudgetRejectedConsumer>();
            bus.AddConsumer<BudgetFailedConsumer>();
            bus.AddConsumer<PaymentConfirmedConsumer>();
            bus.AddConsumer<PaymentFailedConsumer>();
            bus.AddConsumer<ExecutionCompletedConsumer>();
            bus.AddConsumer<ExecutionFailedConsumer>();

            bus.AddEntityFrameworkOutbox<AppDbContext>(outbox =>
            {
                outbox.UsePostgres();
                outbox.UseBusOutbox();
            });

            bus.AddConfigureEndpointsCallback((context, _, endpoint) =>
            {
                endpoint.UseMessageRetry(SagaRetryPolicy.Configure);
                endpoint.UseEntityFrameworkOutbox<AppDbContext>(context);
            });

            bus.ConfigureHealthCheckOptions(health =>
                health.MinimalFailureStatus = HealthStatus.Degraded);

            bus.UsingRabbitMq((context, cfg) =>
            {
                cfg.Host(rabbitMq.Host, rabbitMq.Port, rabbitMq.VirtualHost, host =>
                {
                    host.Username(rabbitMq.Username);
                    host.Password(rabbitMq.Password);
                });

                cfg.ConfigureEndpoints(context);
            });
        });

        return services;
    }
}
