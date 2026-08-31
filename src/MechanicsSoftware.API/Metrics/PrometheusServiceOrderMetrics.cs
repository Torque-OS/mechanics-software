using MechanicsSoftware.Application.Abstractions;
using Prometheus;

namespace MechanicsSoftware.API.Metrics;

public sealed class PrometheusServiceOrderMetrics : IServiceOrderMetrics
{
    private static readonly Gauge Opened = global::Prometheus.Metrics.CreateGauge(
        "mechanics_service_orders_opened_total",
        "Total number of service orders opened.");

    private static readonly Gauge Completed = global::Prometheus.Metrics.CreateGauge(
        "mechanics_service_orders_completed_total",
        "Total number of service orders completed.");

    private static readonly Gauge AverageExecutionTimeHours = global::Prometheus.Metrics.CreateGauge(
        "mechanics_service_orders_average_execution_time_hours",
        "Average service order execution time in hours.");

    private static readonly Gauge ExecutionTimeOrderCount = global::Prometheus.Metrics.CreateGauge(
        "mechanics_service_orders_execution_time_order_count",
        "Number of completed service orders used to calculate average execution time.");

    public void OrderOpened() => Opened.Inc();

    public void OrderCompleted() => Completed.Inc();

    public void SetOrderTotals(long opened, long completed)
    {
        Opened.Set(opened);
        Completed.Set(completed);
    }

    public void SetAverageExecutionTime(double averageHours, int orderCount)
    {
        AverageExecutionTimeHours.Set(averageHours);
        ExecutionTimeOrderCount.Set(orderCount);
    }
}