using MechanicsSoftware.Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace MechanicsSoftware.Application.UseCases.ServiceOrders.Handlers.SagaEvents;

/// <summary>
/// Reacts to the <c>PaymentFailed</c> event by cancelling the order — the Billing Service is
/// responsible for cancelling its own budget as part of the same compensation (ADR-011).
/// </summary>
public sealed class PaymentFailedHandler(IAppDbContext db, IEmailNotifier emailNotifier, ILogger<PaymentFailedHandler> logger)
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
