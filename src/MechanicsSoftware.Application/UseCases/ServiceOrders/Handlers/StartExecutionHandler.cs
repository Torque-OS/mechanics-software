using MechanicsSoftware.Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace MechanicsSoftware.Application.UseCases.ServiceOrders.Handlers;

public sealed class StartExecutionHandler(
    IAppDbContext db,
    IEmailNotifier emailNotifier,
    ILogger<StartExecutionHandler> logger,
    IServiceOrderMetrics? metrics = null)
{
    public async Task<ServiceOrderResponse> ExecuteAsync(
        Guid serviceOrderId, CancellationToken cancellationToken = default)
    {
        var order = await db.ServiceOrders.FindFullAsync(serviceOrderId, cancellationToken);

        order.StartExecution();

        await db.SaveChangesAsync(cancellationToken);

        metrics?.OrderStatusChanged(order.Status.ToString());

        await emailNotifier.TrySendStatusEmailAsync(db, logger, order, cancellationToken);

        return ServiceOrderResponse.From(order);
    }
}
