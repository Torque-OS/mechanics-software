using MechanicsSoftware.Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace MechanicsSoftware.Application.UseCases.ServiceOrders.Handlers.SagaEvents;

/// <summary>
/// Reacts to the <c>BudgetFailed</c> event (Billing could not turn the item snapshot into a
/// budget — e.g. no service item) by cancelling the order — a compensation step of ADR-011
/// (F4-23). The order is still IN_DIAGNOSIS at this point (no budget was ever attached), so this
/// is the one compensation path that cancels from that status instead of a later one. No command
/// back to Billing: it never created a budget to clean up.
/// </summary>
public sealed class BudgetFailedHandler(IAppDbContext db, IEmailNotifier emailNotifier, ILogger<BudgetFailedHandler> logger)
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
