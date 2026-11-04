using MechanicsSoftware.Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace MechanicsSoftware.Application.UseCases.ServiceOrders.Handlers.SagaEvents;

/// <summary>
/// Reacts to the <c>ExecutionCompleted</c> event published by the Execution Service (ADR-011,
/// F4-22) — the last step of the saga's happy path. The order moves to COMPLETED; delivery
/// (<c>Deliver()</c>) stays a manual staff action in this repo's own API, as before.
/// </summary>
public sealed class ExecutionCompletedHandler(IAppDbContext db, IEmailNotifier emailNotifier, ILogger<ExecutionCompletedHandler> logger)
{
    public async Task<ServiceOrderResponse> ExecuteAsync(
        Guid serviceOrderId, CancellationToken cancellationToken = default)
    {
        var order = await db.ServiceOrders.FindFullAsync(serviceOrderId, cancellationToken);

        order.Complete();

        await db.SaveChangesAsync(cancellationToken);

        await emailNotifier.TrySendStatusEmailAsync(db, logger, order, cancellationToken);

        return ServiceOrderResponse.From(order);
    }
}
