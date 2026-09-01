using System.Reflection;
using FluentAssertions;
using MechanicsSoftware.API.Metrics;
using Prometheus;

namespace MechanicsSoftware.UnitTests.API;

public class PrometheusServiceOrderMetricsTests
{
    [Fact]
    public void AverageExecutionTimeByStatus_ShouldBeHistogram()
    {
        var field = typeof(PrometheusServiceOrderMetrics)
            .GetField("AverageExecutionTimeByStatus", BindingFlags.NonPublic | BindingFlags.Static);

        field.Should().NotBeNull();
        field!.FieldType.Should().Be(typeof(Histogram));
    }
}
