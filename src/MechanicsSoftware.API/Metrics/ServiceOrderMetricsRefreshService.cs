using MechanicsSoftware.Application.Abstractions;
using MechanicsSoftware.Domain.ValueObjects;
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
                .Select(order => new
                {
                    order.CreatedAt,
                    order.CompletedAt,
                    order.Status,
                })
                .ToListAsync(cancellationToken);

            var statusHistory = await db.ServiceOrderStatusHistory
                .Select(history => new
                {

                    history.ServiceOrderId,
                    history.Status,
                    history.EnteredAt
                })
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

            var today = DateTime.UtcNow.Date;
            metrics.SetDailyOrderTotals(
                orders.LongCount(order => order.CreatedAt >= today),
                orders.LongCount(order => order.CompletedAt >= today));

            var statuses = new[] { "RECEIVED", "IN_DIAGNOSIS", "AWAITING_APPROVAL", "IN_EXECUTION", "COMPLETED", "DELIVERED", "CANCELLED" };
            foreach (var status in statuses)
            {
                metrics.SetOrderTotalByStatus(
                    status,
                    orders.LongCount(order => order.Status.ToString() == status));
            }

            foreach (var status in statuses)
            {
                var periods = statusHistory
                    .GroupBy(history => history.ServiceOrderId)
                    .SelectMany(historyGroup => historyGroup
                        .OrderBy(history => history.EnteredAt)
                        .Select((history, index) => new
                        {
                            history.Status,
                            Duration = ((index + 1 < historyGroup.Count()
                                ? historyGroup.OrderBy(item => item.EnteredAt).ElementAt(index + 1).EnteredAt
                                : DateTime.UtcNow) - history.EnteredAt).TotalHours
                        }))
                    .Where(history => new ServiceOrderStatus(history.Status).ToString() == status)
                    .Select(history => history.Duration)
                    .ToList();

                metrics.SetAverageExecutionDurationByStatus(
                    status,
                    periods.Count == 0 ? 0 : Math.Round(periods.Average(), 2));
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