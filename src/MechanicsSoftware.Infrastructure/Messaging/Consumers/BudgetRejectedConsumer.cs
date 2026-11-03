using MassTransit;
using MechanicsSoftware.Application.UseCases.ServiceOrders.Handlers.SagaEvents;
using TorqueOS.Contracts.Billing;

namespace MechanicsSoftware.Infrastructure.Messaging.Consumers;

/// <summary>The customer rejected the budget: the order is cancelled and reserved parts are released.</summary>
public sealed class BudgetRejectedConsumer(BudgetRejectedHandler handler) : IConsumer<BudgetRejected>
{
    public Task Consume(ConsumeContext<BudgetRejected> context) =>
        handler.ExecuteAsync(context.Message.ServiceOrderId, context.CancellationToken);
}
