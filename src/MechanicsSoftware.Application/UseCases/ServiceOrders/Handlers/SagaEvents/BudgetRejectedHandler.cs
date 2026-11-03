using MechanicsSoftware.Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace MechanicsSoftware.Application.UseCases.ServiceOrders.Handlers.SagaEvents;

/// <summary>
/// Reacts to the <c>BudgetRejected</c> event (customer rejected the budget, or Billing failed to
/// generate one) by cancelling the order — the first compensation step of ADR-011.
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
