using MechanicsSoftware.Application.Abstractions;
using Microsoft.Extensions.Logging;
using TorqueOS.Contracts.Execution;

namespace MechanicsSoftware.Application.UseCases.ServiceOrders.Handlers.SagaEvents;

/// <summary>
/// Reacts to the <c>PaymentConfirmed</c> event published by the Billing Service after a
/// successful Mercado Pago payment (ADR-011), moving the order to execution and sending
/// <c>EnqueueExecution</c> to the Execution Service — the next step of the happy path (F4-22).
/// </summary>
public sealed class PaymentConfirmedHandler(
    IAppDbContext db, IEventPublisher eventPublisher, IEmailNotifier emailNotifier, ILogger<PaymentConfirmedHandler> logger)
{
    public async Task<ServiceOrderResponse> ExecuteAsync(
        Guid serviceOrderId, CancellationToken cancellationToken = default)
    {
        var order = await db.ServiceOrders.FindFullAsync(serviceOrderId, cancellationToken);

        order.ConfirmPayment();

        // Published before SaveChangesAsync on purpose: the outbox stores the message in the
        // same transaction as the status change, so both are saved or neither is (ADR-011).
        await eventPublisher.PublishAsync(
            new EnqueueExecution(order.Id), correlationId: order.Id, cancellationToken: cancellationToken);

        await db.SaveChangesAsync(cancellationToken);

        await emailNotifier.TrySendStatusEmailAsync(db, logger, order, cancellationToken);

        return ServiceOrderResponse.From(order);
    }
}
