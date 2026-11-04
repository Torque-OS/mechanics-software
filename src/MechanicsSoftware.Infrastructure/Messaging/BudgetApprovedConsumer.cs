using System.Diagnostics.CodeAnalysis;
using MassTransit;
using MechanicsSoftware.Application.Exceptions;
using MechanicsSoftware.Application.UseCases.ServiceOrders.Handlers.SagaEvents;
using MechanicsSoftware.Domain.Exceptions;
using MechanicsSoftware.Infrastructure.Persistence;
using TorqueOS.Contracts.Billing;

namespace MechanicsSoftware.Infrastructure.Messaging;

/// <summary>Saga event from the Billing Service (ADR-011, F4-22).</summary>
public sealed class BudgetApprovedConsumer(BudgetApprovedHandler handler) : IConsumer<BudgetApproved>
{
    public Task Consume(ConsumeContext<BudgetApproved> context) =>
        handler.ExecuteAsync(context.Message.ServiceOrderId, context.CancellationToken);
}

/// <summary>Queue <c>os-service.budget-approved</c>. See <see cref="BudgetGeneratedConsumerDefinition"/> for the retry policy.</summary>
[ExcludeFromCodeCoverage(Justification = "Endpoint wiring needs PostgreSQL and RabbitMQ; exercised manually against the compose stack.")]
public sealed class BudgetApprovedConsumerDefinition : ConsumerDefinition<BudgetApprovedConsumer>
{
    public const string QueueName = "os-service.budget-approved";

    public BudgetApprovedConsumerDefinition() => EndpointName = QueueName;

    protected override void ConfigureConsumer(
        IReceiveEndpointConfigurator endpointConfigurator,
        IConsumerConfigurator<BudgetApprovedConsumer> consumerConfigurator,
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
