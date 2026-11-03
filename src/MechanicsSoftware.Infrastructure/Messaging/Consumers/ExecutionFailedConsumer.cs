using MassTransit;
using MechanicsSoftware.Application.UseCases.ServiceOrders.Handlers.SagaEvents;
using TorqueOS.Contracts.Execution;

namespace MechanicsSoftware.Infrastructure.Messaging.Consumers;

/// <summary>The Execution Service could not finish the repair: the order is cancelled and reserved parts are released.</summary>
public sealed class ExecutionFailedConsumer(ExecutionFailedHandler handler) : IConsumer<ExecutionFailed>
{
    public Task Consume(ConsumeContext<ExecutionFailed> context) =>
        handler.ExecuteAsync(context.Message.ServiceOrderId, context.CancellationToken);
}
