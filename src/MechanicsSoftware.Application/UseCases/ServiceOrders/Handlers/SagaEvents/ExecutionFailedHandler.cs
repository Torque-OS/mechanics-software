using MechanicsSoftware.Application.Abstractions;
using Microsoft.Extensions.Logging;
using TorqueOS.Contracts.Billing;

namespace MechanicsSoftware.Application.UseCases.ServiceOrders.Handlers.SagaEvents;

/// <summary>
/// Reacts to the <c>ExecutionFailed</c> event — the last compensation step of ADR-011 (F4-23):
/// the Execution Service could not finish the repair, so the OS Service cancels the order,
/// releases reserved parts, and commands the Billing Service to refund the payment and cancel
/// the budget (a single <c>CancelBudget</c> — Billing decides whether that means cancelling an
/// open charge or refunding a received one, per its own Budget.Cancel(payment) logic).
/// </summary>
public sealed class ExecutionFailedHandler(
    IAppDbContext db, IEventPublisher eventPublisher, IEmailNotifier emailNotifier, ILogger<ExecutionFailedHandler> logger)
{
    public async Task<ServiceOrderResponse> ExecuteAsync(
        Guid serviceOrderId, CancellationToken cancellationToken = default)
    {
        var order = await db.ServiceOrders.FindFullAsync(serviceOrderId, cancellationToken);

        order.Cancel();

        await db.ReleaseReservedPartsAsync(order, cancellationToken);

        await eventPublisher.PublishAsync(
            new CancelBudget(order.Id), correlationId: order.Id, cancellationToken: cancellationToken);

        await db.SaveChangesAsync(cancellationToken);

        await emailNotifier.TrySendStatusEmailAsync(db, logger, order, cancellationToken);

        return ServiceOrderResponse.From(order);
    }
}
