using MechanicsSoftware.Application.Abstractions;
using MechanicsSoftware.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace MechanicsSoftware.Application.UseCases.ServiceOrders.Handlers;

public sealed class GetAverageExecutionTimeHandler(IAppDbContext db, IServiceOrderMetrics? metrics = null)
{
    public async Task<AverageExecutionTimeResponse> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var completedStatus = new ServiceOrderStatus(ServiceOrderStatus.Status.Completed);
        var deliveredStatus = new ServiceOrderStatus(ServiceOrderStatus.Status.Delivered);

        var completedOrders = await db.ServiceOrders
            .Where(o => o.CompletedAt != null)
            .Where(o => o.Status == completedStatus || o.Status == deliveredStatus)
            .Select(o => new { o.CreatedAt, o.CompletedAt })
            .ToListAsync(cancellationToken);

        if (completedOrders.Count == 0)
        {
            metrics?.SetAverageExecutionTime(0, 0);
            return new AverageExecutionTimeResponse(0, 0);
        }

        var averageHours = completedOrders
            .Average(o => (o.CompletedAt!.Value - o.CreatedAt).TotalHours);

        var roundedAverageHours = Math.Round(averageHours, 2);
        metrics?.SetAverageExecutionTime(roundedAverageHours, completedOrders.Count);

        return new AverageExecutionTimeResponse(roundedAverageHours, completedOrders.Count);
    }
}
