using System.Diagnostics.CodeAnalysis;
using MassTransit;
using MechanicsSoftware.Application.Exceptions;
using MechanicsSoftware.Application.UseCases.ServiceOrders.Handlers.SagaEvents;
using MechanicsSoftware.Domain.Exceptions;
using MechanicsSoftware.Infrastructure.Persistence;
using TorqueOS.Contracts.Billing;

namespace MechanicsSoftware.Infrastructure.Messaging;

/// <summary>Saga compensation event from the Billing Service (ADR-011, F4-23).</summary>
public sealed class BudgetFailedConsumer(BudgetFailedHandler handler) : IConsumer<BudgetFailed>
{
    public Task Consume(ConsumeContext<BudgetFailed> context) =>
        handler.ExecuteAsync(context.Message.ServiceOrderId, context.CancellationToken);
}

/// <summary>Queue <c>os-service.budget-failed</c>. See <see cref="BudgetGeneratedConsumerDefinition"/> for the retry policy.</summary>
[ExcludeFromCodeCoverage(Justification = "Endpoint wiring needs PostgreSQL and RabbitMQ; exercised manually against the compose stack.")]
public sealed class BudgetFailedConsumerDefinition : ConsumerDefinition<BudgetFailedConsumer>
{
    public const string QueueName = "os-service.budget-failed";

    public BudgetFailedConsumerDefinition() => EndpointName = QueueName;

    protected override void ConfigureConsumer(
        IReceiveEndpointConfigurator endpointConfigurator,
        IConsumerConfigurator<BudgetFailedConsumer> consumerConfigurator,
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
