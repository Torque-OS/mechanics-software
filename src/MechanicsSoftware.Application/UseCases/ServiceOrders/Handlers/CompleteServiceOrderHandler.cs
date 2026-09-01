using MechanicsSoftware.Application.Abstractions;
using MechanicsSoftware.Application.Exceptions;
using MechanicsSoftware.Domain.Entities;
using MechanicsSoftware.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace MechanicsSoftware.Application.UseCases.ServiceOrders.Handlers;

public sealed class CompleteServiceOrderHandler(
    IAppDbContext db,
    IEmailNotifier emailNotifier,
    ILogger<CompleteServiceOrderHandler> logger,
    IServiceOrderMetrics? metrics = null)
{
    public async Task<ServiceOrderResponse> ExecuteAsync(
        Guid serviceOrderId, CancellationToken cancellationToken = default)
    {
        var order = await db.ServiceOrders.FindFullAsync(serviceOrderId, cancellationToken);

        order.Complete();

        var availablePartItems = order.PartItems
            .Where(p => p.Availability == PartAvailability.Available);

        foreach (var partItem in availablePartItems)
        {
            var part = await db.Parts.FindAsync([partItem.PartId], cancellationToken)
                ?? throw new NotFoundException(nameof(Part), partItem.PartId);

            part.ConfirmUsage(partItem.Quantity, serviceOrderId);
        }

        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Service order completed {EventName} {ServiceOrderId} {OccurredAt}",
            "service_order.completed",
            order.Id,
            order.CompletedAt);
        metrics?.OrderCompleted();
        metrics?.OrderStatusChanged(order.Status.ToString());

        var averageHours = order.CompletedAt.HasValue && order.CreatedAt != default
            ? (order.CompletedAt.Value - order.CreatedAt).TotalHours
            : 0;
        metrics?.SetAverageExecutionTimeByStatus(order.Status.ToString(), averageHours);

        await emailNotifier.TrySendStatusEmailAsync(db, logger, order, cancellationToken);

        return ServiceOrderResponse.From(order);
    }
}
