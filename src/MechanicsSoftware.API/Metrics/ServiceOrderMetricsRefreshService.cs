using MechanicsSoftware.Application.Abstractions;
using MechanicsSoftware.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MechanicsSoftware.API.Metrics;

public sealed class ServiceOrderMetricsRefreshService(
    IServiceScopeFactory scopeFactory,
    IServiceOrderMetrics metrics,
    ILogger<ServiceOrderMetricsRefreshService> logger) : BackgroundService
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RefreshAsync(stoppingToken);

        using var timer = new PeriodicTimer(RefreshInterval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
            await RefreshAsync(stoppingToken);
    }

    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var orders = await db.ServiceOrders
                .Where(order => order.CompletedAt != null || order.CreatedAt != default)
                .Select(order => new { order.CreatedAt, order.CompletedAt, order.Status })
                .ToListAsync(cancellationToken);

            var completedOrders = orders
                .Where(order => order.CompletedAt != null)
                .ToList();
            var averageHours = completedOrders.Count == 0
                ? 0
                : completedOrders.Average(order =>
                    (order.CompletedAt!.Value - order.CreatedAt).TotalHours);

            metrics.SetOrderTotals(orders.Count, completedOrders.Count);
            metrics.SetAverageExecutionTime(
                Math.Round(averageHours, 2),
                completedOrders.Count);

            var ordersByStatus = orders.GroupBy(o => o.Status.ToString());
            foreach (var statusGroup in ordersByStatus)
            {
                metrics.SetOrderTotalByStatus(statusGroup.Key, statusGroup.LongCount());
                
                var completedInStatus = statusGroup
                    .Where(o => o.CompletedAt != null)
                    .ToList();
                
                var avgDurationHours = completedInStatus.Count == 0
                    ? 0
                    : completedInStatus.Average(o => (o.CompletedAt!.Value - o.CreatedAt).TotalHours);
                metrics.SetAverageExecutionDurationByStatus(statusGroup.Key, avgDurationHours);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Failed to refresh service order metrics");
        }
    }
}