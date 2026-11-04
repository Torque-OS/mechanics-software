using System.Diagnostics.CodeAnalysis;
using MassTransit;
using MechanicsSoftware.Application.Abstractions;
using MechanicsSoftware.Infrastructure.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace MechanicsSoftware.Infrastructure.Messaging;

[ExcludeFromCodeCoverage]
public static class MessagingSetup
{
    public static IServiceCollection AddMessaging(this IServiceCollection services, IConfiguration configuration)
    {
        var rabbitMq = RabbitMqSettings.FromConfiguration(configuration);

        services.AddScoped<IEventPublisher, MassTransitEventPublisher>();

        services.AddMassTransit(bus =>
        {
            bus.SetKebabCaseEndpointNameFormatter();

            // Saga consumers (ADR-011) — happy path (F4-22) and compensation (F4-23).
            bus.AddConsumer<BudgetGeneratedConsumer, BudgetGeneratedConsumerDefinition>();
            bus.AddConsumer<BudgetApprovedConsumer, BudgetApprovedConsumerDefinition>();
            bus.AddConsumer<PaymentConfirmedConsumer, PaymentConfirmedConsumerDefinition>();
            bus.AddConsumer<ExecutionCompletedConsumer, ExecutionCompletedConsumerDefinition>();
            bus.AddConsumer<BudgetFailedConsumer, BudgetFailedConsumerDefinition>();
            bus.AddConsumer<BudgetRejectedConsumer, BudgetRejectedConsumerDefinition>();
            bus.AddConsumer<PaymentFailedConsumer, PaymentFailedConsumerDefinition>();
            bus.AddConsumer<ExecutionFailedConsumer, ExecutionFailedConsumerDefinition>();

            // Transactional outbox (ADR-011): published messages are stored in the OS Service
            // database in the same transaction as the entity changes and delivered afterwards.
            bus.AddEntityFrameworkOutbox<AppDbContext>(outbox =>
            {
                outbox.UsePostgres();
                outbox.UseBusOutbox();
            });

            // A broker outage must not make /health fail: the readiness/liveness probes would
            // restart the pods, while the outbox already keeps messages until RabbitMQ is back.
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
