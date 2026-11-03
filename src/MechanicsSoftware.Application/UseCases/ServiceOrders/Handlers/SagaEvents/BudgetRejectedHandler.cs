using MechanicsSoftware.Application.Abstractions;
using MechanicsSoftware.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace MechanicsSoftware.Application.UseCases.ServiceOrders.Handlers.SagaEvents;

/// <summary>
/// Reacts to the <c>BudgetRejected</c> event (customer rejected the budget) and to the
/// <c>BudgetFailed</c> event (Billing could not generate one) by cancelling the order — the first
/// compensation step of ADR-011.
/// </summary>
public sealed class BudgetRejectedHandler(IAppDbContext db, IEmailNotifier emailNotifier, ILogger<BudgetRejectedHandler> logger)
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

        await db.SaveChangesAsync(cancellationToken);

        await emailNotifier.TrySendStatusEmailAsync(db, logger, order, cancellationToken);

        return ServiceOrderResponse.From(order);
    }
}
