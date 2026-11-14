using MechanicsSoftware.Application.Abstractions;
using MechanicsSoftware.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MechanicsSoftware.API.Metrics;

public sealed class ServiceOrderMetricsRefreshService(
    IServiceScopeFactory scopeFactory,
    IServiceOrderMetrics metrics,
    IServiceOrderStatusHistoryStore historyStore,
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
                .Select(order => new
                {
                    order.Id,
                    order.CreatedAt,
                    order.CompletedAt,
                    order.Status,
                })
                .ToListAsync(cancellationToken);

            var statusHistory = await historyStore.GetAsync(
                orders.Select(order => order.Id).ToArray(),
                cancellationToken);

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

            var today = DateTime.UtcNow.Date;
            metrics.SetDailyOrderTotals(
                orders.LongCount(order => order.CreatedAt >= today),
                orders.LongCount(order => order.CompletedAt >= today));

            var statuses = new[] { "RECEIVED", "IN_DIAGNOSIS", "AWAITING_APPROVAL", "AWAITING_PAYMENT", "IN_EXECUTION", "COMPLETED", "DELIVERED", "CANCELLED" };
            foreach (var status in statuses)
            {
                metrics.SetOrderTotalByStatus(
                    status,
                    orders.LongCount(order => order.Status.ToString() == status));
            }

            var periodsByStatus = statusHistory
                .GroupBy(history => history.ServiceOrderId)
                .SelectMany(historyGroup =>
                {
                    var orderedHistory = historyGroup.OrderBy(history => history.EnteredAt).ToArray();
                    return orderedHistory.Select((history, index) => new
                    {
                        history.Status,
                        Duration = ((index + 1 < orderedHistory.Length
                            ? orderedHistory[index + 1].EnteredAt
                            : DateTime.UtcNow) - history.EnteredAt).TotalHours
                    });
                })
                .GroupBy(history => history.Status)
                .ToDictionary(group => group.Key, group => group.Select(history => history.Duration).ToList());

            foreach (var status in statuses)
            {
                periodsByStatus.TryGetValue(status, out var periods);
                metrics.SetAverageExecutionDurationByStatus(
                    status,
                    periods is null || periods.Count == 0 ? 0 : Math.Round(periods.Average(), 2));
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