using MechanicsSoftware.Application.Abstractions;
using MechanicsSoftware.Domain.ValueObjects;
using MechanicsSoftware.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MechanicsSoftware.API.Metrics;

public sealed class ServiceOrderMetricsRefreshService(
    IServiceScopeFactory scopeFactory,
    IServiceOrderMetrics metrics,
    IServiceOrderStatusHistoryStore historyStore,
    ISagaMetrics sagaMetrics,
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

            var statusHistory = (await historyStore.GetAsync(
                    orders.Select(order => order.Id).ToArray(),
                    cancellationToken))
                .Select(ToTimelineEntry)
                .OfType<StatusHistoryEntry>()
                .ToList();

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

            // Every status, including AWAITING_PAYMENT (Fase 4), so the saga dashboard sees each stage.
            foreach (var status in StatusTimelineMetrics.AllStatuses)
            {
                metrics.SetOrderTotalByStatus(
                    status,
                    orders.LongCount(order => order.Status.ToString() == status));
            }

            var timeline = StatusTimelineMetrics.Calculate(statusHistory, DateTime.UtcNow);

            foreach (var (status, statusHours) in timeline.AverageHoursByStatus)
                metrics.SetAverageExecutionDurationByStatus(status, statusHours);

            foreach (var (stage, count) in timeline.CompensationsByStage)
                sagaMetrics.SetCompensationsByStage(stage, count);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Failed to refresh service order metrics");
        }
    }

    // The history store keeps the status as its API text (IN_DIAGNOSIS); the timeline works on the enum.
    private static StatusHistoryEntry? ToTimelineEntry(ServiceOrderStatusHistoryRecord record) =>
        Enum.TryParse<ServiceOrderStatus.Status>(record.Status.Replace("_", ""), ignoreCase: true, out var status)
            ? new StatusHistoryEntry(record.ServiceOrderId, status, record.EnteredAt)
            : null;
}
