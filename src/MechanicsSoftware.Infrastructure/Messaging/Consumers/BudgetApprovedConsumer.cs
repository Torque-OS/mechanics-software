using MassTransit;
using MechanicsSoftware.Application.UseCases.ServiceOrders.Handlers.SagaEvents;
using TorqueOS.Contracts.Billing;

namespace MechanicsSoftware.Infrastructure.Messaging.Consumers;

/// <summary>The customer approved the budget: the order moves to AWAITING_PAYMENT.</summary>
public sealed class BudgetApprovedConsumer(BudgetApprovedHandler handler) : IConsumer<BudgetApproved>
{
    public Task Consume(ConsumeContext<BudgetApproved> context) =>
        handler.ExecuteAsync(context.Message.ServiceOrderId, context.CancellationToken);
}
