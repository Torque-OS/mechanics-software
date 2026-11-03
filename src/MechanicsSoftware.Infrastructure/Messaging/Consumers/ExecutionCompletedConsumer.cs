using MassTransit;
using MechanicsSoftware.Application.UseCases.ServiceOrders.Handlers.SagaEvents;
using TorqueOS.Contracts.Execution;

namespace MechanicsSoftware.Infrastructure.Messaging.Consumers;

/// <summary>The Execution Service finished the repair: the order moves to COMPLETED.</summary>
public sealed class ExecutionCompletedConsumer(ExecutionCompletedHandler handler) : IConsumer<ExecutionCompleted>
{
    public Task Consume(ConsumeContext<ExecutionCompleted> context) =>
        handler.ExecuteAsync(context.Message.ServiceOrderId, context.CancellationToken);
}
