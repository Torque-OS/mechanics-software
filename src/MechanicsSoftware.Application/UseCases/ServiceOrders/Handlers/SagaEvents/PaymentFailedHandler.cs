using MechanicsSoftware.Application.Abstractions;
using Microsoft.Extensions.Logging;
using TorqueOS.Contracts.Billing;

namespace MechanicsSoftware.Application.UseCases.ServiceOrders.Handlers.SagaEvents;

/// <summary>
/// Reacts to the <c>PaymentFailed</c> event by cancelling the order and commanding the Billing
/// Service to cancel its budget (ADR-011, F4-23) — Billing is a participant, not a second
/// orchestrator: it has no way to know the order was cancelled unless the OS Service, as the
/// Saga's single decision point, tells it to.
/// </summary>
public sealed class PaymentFailedHandler(
    IAppDbContext db, IEventPublisher eventPublisher, IEmailNotifier emailNotifier, ILogger<PaymentFailedHandler> logger)
{
    public async Task<ServiceOrderResponse> ExecuteAsync(
        Guid serviceOrderId, CancellationToken cancellationToken = default)
    {
        var order = await db.ServiceOrders.FindFullAsync(serviceOrderId, cancellationToken);

        order.Cancel();

        await db.ReleaseReservedPartsAsync(order, cancellationToken);

        // Published before SaveChangesAsync on purpose: the outbox stores the message in the
        // same transaction as the status change, so both are saved or neither is (ADR-011).
        await eventPublisher.PublishAsync(
            new CancelBudget(order.Id), correlationId: order.Id, cancellationToken: cancellationToken);

        await db.SaveChangesAsync(cancellationToken);

        await emailNotifier.TrySendStatusEmailAsync(db, logger, order, cancellationToken);

        return ServiceOrderResponse.From(order);
    }
}
