using System.Reflection;
using FluentAssertions;
using MechanicsSoftware.API.Metrics;
using Prometheus;

namespace MechanicsSoftware.UnitTests.API;

public class PrometheusServiceOrderMetricsTests
{
    private readonly PrometheusServiceOrderMetrics metrics = new();

    [Fact]
    public void ExecutionDurationByStatus_ShouldBeHistogram()
    {
        var field = typeof(PrometheusServiceOrderMetrics)
            .GetField("ExecutionDurationByStatus", BindingFlags.NonPublic | BindingFlags.Static);

        field.Should().NotBeNull();
        field!.FieldType.Should().Be(typeof(Histogram));
    }

    [Fact]
    public void OrderCounters_CanBeUpdated()
    {
        metrics.OrderOpened();
        metrics.OrderCompleted();
        metrics.SetOrderTotals(10, 5);
        metrics.SetDailyOrderTotals(2, 1);
        metrics.SetAverageExecutionTime(4.5, 5);
    }

    [Fact]
    public void StatusMetrics_IgnoreBlankStatus()
    {
        metrics.ObserveExecutionDurationByStatus(" ", 1);
        metrics.SetOrderTotalByStatus(" ", 1);
        metrics.SetAverageExecutionDurationByStatus(" ", 1);
    }

    [Fact]
    public void StatusMetrics_AcceptValidStatus()
    {
        metrics.ObserveExecutionDurationByStatus("COMPLETED", 1);
        metrics.SetOrderTotalByStatus("COMPLETED", 1);
        metrics.SetAverageExecutionDurationByStatus("COMPLETED", 1);
    }

    [Fact]
    public void HttpMetrics_CanBeRecorded()
    {
        metrics.RecordHttpError("GET", "/health", 500);
        metrics.RecordHttpLatency(12.5, "GET", "/health", 200);
    }
}
