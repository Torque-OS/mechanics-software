using MechanicsSoftware.Application.Abstractions;
using MechanicsSoftware.Domain.ValueObjects;
using Microsoft.Extensions.Logging;
using TorqueOS.Contracts.Billing;

namespace MechanicsSoftware.Application.UseCases.ServiceOrders.Handlers.SagaEvents;

/// <summary>
/// Reacts to the <c>ExecutionFailed</c> event — the last compensation step of ADR-011: the
/// Execution Service could not finish the repair, the Billing Service refunds the payment and
/// cancels the budget, and the OS Service cancels the order and releases reserved parts.
/// </summary>
public sealed class ExecutionFailedHandler(
    IAppDbContext db, IEventPublisher eventPublisher, IEmailNotifier emailNotifier, ILogger<ExecutionFailedHandler> logger)
{
    public async Task<ServiceOrderResponse> ExecuteAsync(
        Guid serviceOrderId, CancellationToken cancellationToken = default)
    {
        var order = await db.ServiceOrders.FindFullAsync(serviceOrderId, cancellationToken);

        // Idempotent: a redelivered or duplicated event finds the order already cancelled.
        if (order.Status.Is(ServiceOrderStatus.Status.Cancelled))
            return ServiceOrderResponse.From(order);

        order.Cancel();

        await db.ReleaseReservedPartsAsync(order, cancellationToken);

        await eventPublisher.PublishAsync(new CancelBudget(order.Id), order.Id, cancellationToken);

        await db.SaveChangesAsync(cancellationToken);

        await emailNotifier.TrySendStatusEmailAsync(db, logger, order, cancellationToken);

        return ServiceOrderResponse.From(order);
    }
}
