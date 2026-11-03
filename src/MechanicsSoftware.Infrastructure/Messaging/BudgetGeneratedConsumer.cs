using System.Diagnostics.CodeAnalysis;
using MassTransit;
using MechanicsSoftware.Application.Exceptions;
using MechanicsSoftware.Application.UseCases.ServiceOrders.Handlers.SagaEvents;
using MechanicsSoftware.Domain.Exceptions;
using MechanicsSoftware.Infrastructure.Persistence;
using TorqueOS.Contracts.Billing;

namespace MechanicsSoftware.Infrastructure.Messaging;

/// <summary>Saga event from the Billing Service (ADR-011, F4-22).</summary>
public sealed class BudgetGeneratedConsumer(BudgetGeneratedHandler handler) : IConsumer<BudgetGenerated>
{
    public Task Consume(ConsumeContext<BudgetGenerated> context) =>
        handler.ExecuteAsync(
            context.Message.ServiceOrderId, context.Message.BudgetId, context.Message.TotalInCents,
            context.CancellationToken);
}

/// <summary>
/// Queue <c>os-service.budget-generated</c>. A transient failure is retried; an unknown order or
/// a wrong-status conflict (e.g. redelivered after the order already moved on) is not — it goes
/// straight to <c>os-service.budget-generated_error</c>. The EF Core inbox drops redeliveries of
/// an already handled <c>MessageId</c>.
/// </summary>
[ExcludeFromCodeCoverage(Justification = "Endpoint wiring needs PostgreSQL and RabbitMQ; exercised manually against the compose stack.")]
public sealed class BudgetGeneratedConsumerDefinition : ConsumerDefinition<BudgetGeneratedConsumer>
{
    public const string QueueName = "os-service.budget-generated";

    public BudgetGeneratedConsumerDefinition() => EndpointName = QueueName;

    protected override void ConfigureConsumer(
        IReceiveEndpointConfigurator endpointConfigurator,
        IConsumerConfigurator<BudgetGeneratedConsumer> consumerConfigurator,
        IRegistrationContext context)
    {
        endpointConfigurator.UseMessageRetry(retry =>
        {
            retry.Intervals(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15));
            retry.Ignore<DomainException>();
            retry.Ignore<NotFoundException>();
        });

        endpointConfigurator.UseEntityFrameworkOutbox<AppDbContext>(context);
    }
}
