using System.Reflection;
using FluentAssertions;
using MechanicsSoftware.API.Metrics;
using Prometheus;

namespace MechanicsSoftware.UnitTests.API;

public class PrometheusServiceOrderMetricsTests
{
    [Fact]
    public void ExecutionDurationByStatus_ShouldBeHistogram()
    {
        var field = typeof(PrometheusServiceOrderMetrics)
            .GetField("ExecutionDurationByStatus", BindingFlags.NonPublic | BindingFlags.Static);

        field.Should().NotBeNull();
        field!.FieldType.Should().Be(typeof(Histogram));
    }
}
