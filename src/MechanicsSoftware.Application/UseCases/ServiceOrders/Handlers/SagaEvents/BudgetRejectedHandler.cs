using MechanicsSoftware.Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace MechanicsSoftware.Application.UseCases.ServiceOrders.Handlers.SagaEvents;

/// <summary>
/// Reacts to the <c>BudgetRejected</c> event (customer rejected the budget) by cancelling the
/// order — a compensation step of ADR-011. No command back to Billing: it already knows its own
/// budget was rejected. See <see cref="BudgetFailedHandler"/> for the earlier failure case
/// (Billing couldn't generate a budget at all).
/// </summary>
public sealed class BudgetRejectedHandler(IAppDbContext db, IEmailNotifier emailNotifier, ILogger<BudgetRejectedHandler> logger)
{
    public async Task<ServiceOrderResponse> ExecuteAsync(
        Guid serviceOrderId, CancellationToken cancellationToken = default)
    {
        var order = await db.ServiceOrders.FindFullAsync(serviceOrderId, cancellationToken);

        order.Cancel();

        await db.ReleaseReservedPartsAsync(order, cancellationToken);

        await db.SaveChangesAsync(cancellationToken);

        await emailNotifier.TrySendStatusEmailAsync(db, logger, order, cancellationToken);

        return ServiceOrderResponse.From(order);
    }
}
