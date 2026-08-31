using MechanicsSoftware.Application.Abstractions;
using Prometheus;

namespace MechanicsSoftware.API.Metrics;

public sealed class PrometheusServiceOrderMetrics : IServiceOrderMetrics
{
    private static readonly Counter Opened = global::Prometheus.Metrics.CreateCounter(
        "mechanics_service_orders_opened_total",
        "Total number of service orders opened.");

    private static readonly Counter Completed = global::Prometheus.Metrics.CreateCounter(
        "mechanics_service_orders_completed_total",
        "Total number of service orders completed.");

    public void OrderOpened() => Opened.Inc();

    public void OrderCompleted() => Completed.Inc();
}