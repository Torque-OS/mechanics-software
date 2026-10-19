using MechanicsSoftware.Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace MechanicsSoftware.Application.UseCases.ServiceOrders.Handlers.SagaEvents;

/// <summary>
/// Reacts to the <c>ExecutionFailed</c> event — the last compensation step of ADR-011: the
/// Execution Service could not finish the repair, the Billing Service refunds the payment and
/// cancels the budget, and the OS Service cancels the order and releases reserved parts.
/// </summary>
public sealed class ExecutionFailedHandler(IAppDbContext db, IEmailNotifier emailNotifier, ILogger<ExecutionFailedHandler> logger)
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
