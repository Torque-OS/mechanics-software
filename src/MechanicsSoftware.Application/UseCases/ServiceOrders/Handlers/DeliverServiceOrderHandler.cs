using MechanicsSoftware.Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace MechanicsSoftware.Application.UseCases.ServiceOrders.Handlers;

public sealed class DeliverServiceOrderHandler(
    IAppDbContext db,
    IEmailNotifier emailNotifier,
    ILogger<DeliverServiceOrderHandler> logger,
    IServiceOrderStatusHistoryRepository? history = null)
{
    public async Task<ServiceOrderResponse> ExecuteAsync(
        Guid serviceOrderId, CancellationToken cancellationToken = default)
    {
        var order = await db.ServiceOrders.FindFullAsync(serviceOrderId, cancellationToken);

        order.Deliver();

        await db.SaveChangesAsync(cancellationToken);
        await history.AppendStatusHistoryAsync(order, DateTime.UtcNow, cancellationToken);

        await emailNotifier.TrySendStatusEmailAsync(db, logger, order, cancellationToken);

        return ServiceOrderResponse.From(order);
    }
}
