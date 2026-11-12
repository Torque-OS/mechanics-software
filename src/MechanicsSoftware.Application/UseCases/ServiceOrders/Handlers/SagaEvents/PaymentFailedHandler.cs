using MechanicsSoftware.Application.Abstractions;
using MechanicsSoftware.Domain.ValueObjects;
using Microsoft.Extensions.Logging;
using TorqueOS.Contracts.Billing;

namespace MechanicsSoftware.Application.UseCases.ServiceOrders.Handlers.SagaEvents;

public sealed class PaymentFailedHandler(
    IAppDbContext db, IEventPublisher eventPublisher, IEmailNotifier emailNotifier, ILogger<PaymentFailedHandler> logger)
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
