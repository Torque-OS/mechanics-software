using MassTransit;
using MechanicsSoftware.Application.UseCases.ServiceOrders.Handlers.SagaEvents;
using TorqueOS.Contracts.Billing;

namespace MechanicsSoftware.Infrastructure.Messaging.Consumers;

/// <summary>Billing generated the budget: the order moves to AWAITING_APPROVAL.</summary>
public sealed class BudgetGeneratedConsumer(BudgetGeneratedHandler handler) : IConsumer<BudgetGenerated>
{
    public Task Consume(ConsumeContext<BudgetGenerated> context) =>
        handler.ExecuteAsync(context.Message.ServiceOrderId, context.Message.BudgetId, context.Message.TotalInCents, context.CancellationToken);
}
