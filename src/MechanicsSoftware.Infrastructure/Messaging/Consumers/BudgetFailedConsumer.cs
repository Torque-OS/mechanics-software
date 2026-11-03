using MassTransit;
using MechanicsSoftware.Application.UseCases.ServiceOrders.Handlers.SagaEvents;
using TorqueOS.Contracts.Billing;

namespace MechanicsSoftware.Infrastructure.Messaging.Consumers;

/// <summary>Billing could not generate the budget: same compensation as a rejection (cancel, release parts).</summary>
public sealed class BudgetFailedConsumer(BudgetRejectedHandler handler) : IConsumer<BudgetFailed>
{
    public Task Consume(ConsumeContext<BudgetFailed> context) =>
        handler.ExecuteAsync(context.Message.ServiceOrderId, context.CancellationToken);
}
