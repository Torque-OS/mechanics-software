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

    private static readonly Counter ServiceOrdersByStatus = global::Prometheus.Metrics
        .CreateCounter(
            "mechanics_service_orders_by_status_total",
            "Number of service orders by status.",
            new CounterConfiguration { LabelNames = new[] { "status" } });

    private static readonly Histogram ExecutionDurationByStatus = global::Prometheus.Metrics
        .CreateHistogram(
            "mechanics_service_orders_execution_duration_hours_by_status",
            "Execution duration in hours by status.",
            new HistogramConfiguration
            {
                Buckets = new[] { 0.25, 0.5, 1, 2, 4, 8, 12, 24, 48, 72 },
                LabelNames = new[] { "status" }
            });

    private static readonly Gauge ServiceOrderTotalsByStatus = global::Prometheus.Metrics
        .CreateGauge(
            "mechanics_service_orders_current_by_status",
            "Current number of service orders by status.",
            new GaugeConfiguration { LabelNames = new[] { "status" } });

    private static readonly Gauge AverageExecutionDurationByStatus = global::Prometheus.Metrics
        .CreateGauge(
            "mechanics_service_orders_average_execution_duration_hours_by_status",
            "Average execution duration in hours by status.",
            new GaugeConfiguration { LabelNames = new[] { "status" } });

    private static readonly Counter HttpErrors = global::Prometheus.Metrics
        .CreateCounter(
            "mechanics_http_errors_total",
            "Total number of HTTP errors.",
            new CounterConfiguration { LabelNames = new[] { "method", "path", "status_code" } });

    private static readonly Histogram HttpRequestDuration = global::Prometheus.Metrics
        .CreateHistogram(
            "mechanics_http_request_duration_ms",
            "HTTP request duration in milliseconds.",
            new HistogramConfiguration
            {
                Buckets = new[] { 10d, 25d, 50d, 100d, 250d, 500d, 1000d, 2500d, 5000d, 10000d },
                LabelNames = new[] { "method", "path", "status_code" }
            });

    public void OrderOpened() => Opened.Inc();

    public void OrderCompleted() => Completed.Inc();

    public void OrderStatusChanged(string status)
    {
        var normalized = status.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
            return;

        ServiceOrdersByStatus.WithLabels(normalized).Inc();
    }

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

    public void ObserveExecutionDurationByStatus(string status, double durationHours)
    {
        var normalized = status.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
            return;

        ExecutionDurationByStatus.WithLabels(normalized).Observe(durationHours);
    }

    public void SetOrderTotalByStatus(string status, long count)
    {
        var normalized = status.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
            return;

        ServiceOrderTotalsByStatus.WithLabels(normalized).Set(count);
    }

    public void SetAverageExecutionDurationByStatus(string status, double averageDurationHours)
    {
        var normalized = status.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
            return;

        AverageExecutionDurationByStatus.WithLabels(normalized).Set(averageDurationHours);
    }

    public void RecordHttpError(string method, string path, int statusCode)
    {
        HttpErrors.WithLabels(method, path, statusCode.ToString()).Inc();
    }

    public void RecordHttpLatency(double durationMs, string method, string path, int statusCode)
    {
        HttpRequestDuration.WithLabels(method, path, statusCode.ToString()).Observe(durationMs);
    }
}