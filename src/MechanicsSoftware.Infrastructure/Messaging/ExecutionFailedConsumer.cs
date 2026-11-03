using System.Diagnostics.CodeAnalysis;
using MassTransit;
using MechanicsSoftware.Application.Exceptions;
using MechanicsSoftware.Application.UseCases.ServiceOrders.Handlers.SagaEvents;
using MechanicsSoftware.Domain.Exceptions;
using MechanicsSoftware.Infrastructure.Persistence;
using TorqueOS.Contracts.Execution;

namespace MechanicsSoftware.Infrastructure.Messaging;

/// <summary>
/// Saga compensation event from the Execution Service (ADR-011, F4-23) — the last step. The
/// handler also publishes <c>CancelBudget</c> to Billing (see <see cref="ExecutionFailedHandler"/>).
/// </summary>
public sealed class ExecutionFailedConsumer(ExecutionFailedHandler handler) : IConsumer<ExecutionFailed>
{
    public Task Consume(ConsumeContext<ExecutionFailed> context) =>
        handler.ExecuteAsync(context.Message.ServiceOrderId, context.CancellationToken);
}

/// <summary>Queue <c>os-service.execution-failed</c>. See <see cref="BudgetGeneratedConsumerDefinition"/> for the retry policy.</summary>
[ExcludeFromCodeCoverage(Justification = "Endpoint wiring needs PostgreSQL and RabbitMQ; exercised manually against the compose stack.")]
public sealed class ExecutionFailedConsumerDefinition : ConsumerDefinition<ExecutionFailedConsumer>
{
    public const string QueueName = "os-service.execution-failed";

    public ExecutionFailedConsumerDefinition() => EndpointName = QueueName;

    protected override void ConfigureConsumer(
        IReceiveEndpointConfigurator endpointConfigurator,
        IConsumerConfigurator<ExecutionFailedConsumer> consumerConfigurator,
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
