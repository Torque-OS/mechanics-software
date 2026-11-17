using FluentAssertions;
using MechanicsSoftware.API.Metrics;
using MechanicsSoftware.Domain.ValueObjects;
using S = MechanicsSoftware.Domain.ValueObjects.ServiceOrderStatus.Status;

namespace MechanicsSoftware.UnitTests.API;

public class StatusTimelineMetricsTests
{
    private static readonly DateTime T0 = new(2026, 11, 1, 8, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void AllStatuses_IncludesAwaitingPayment()
    {
        StatusTimelineMetrics.AllStatuses.Should().Contain("AWAITING_PAYMENT")
            .And.HaveCount(Enum.GetValues<S>().Length);
    }

    [Fact]
    public void Calculate_AveragesTimeSpentInEachStatus_CurrentStatusCountsUntilNow()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var history = new[]
        {
            new StatusHistoryEntry(a, S.Received, T0),
            new StatusHistoryEntry(a, S.InDiagnosis, T0.AddHours(1)),
            new StatusHistoryEntry(b, S.Received, T0),
            new StatusHistoryEntry(b, S.InDiagnosis, T0.AddHours(3)),
        };

        var result = StatusTimelineMetrics.Calculate(history, now: T0.AddHours(5));

        result.AverageHoursByStatus["RECEIVED"].Should().Be(2);      // (1 + 3) / 2
        result.AverageHoursByStatus["IN_DIAGNOSIS"].Should().Be(3);  // (4 + 2) / 2
        result.AverageHoursByStatus["IN_EXECUTION"].Should().Be(0);
    }

    [Fact]
    public void Calculate_CountsCompensationsByTheStageTheOrderWasCancelledFrom()
    {
        var history = new List<StatusHistoryEntry>();
        void Order(params S[] statuses)
        {
            var id = Guid.NewGuid();
            history.AddRange(statuses.Select((status, i) => new StatusHistoryEntry(id, status, T0.AddMinutes(i))));
        }

        Order(S.Received, S.InDiagnosis, S.AwaitingApproval, S.Cancelled);                                  // budget rejected
        Order(S.Received, S.InDiagnosis, S.AwaitingApproval, S.AwaitingPayment, S.Cancelled);               // payment failed
        Order(S.Received, S.InDiagnosis, S.AwaitingApproval, S.AwaitingPayment, S.InExecution, S.Cancelled); // execution failed
        Order(S.Received, S.InDiagnosis, S.AwaitingApproval, S.AwaitingPayment, S.InExecution, S.Cancelled);
        Order(S.Received, S.InDiagnosis, S.AwaitingApproval, S.AwaitingPayment, S.InExecution, S.Completed); // happy path

        var result = StatusTimelineMetrics.Calculate(history, T0.AddHours(1));

        result.CompensationsByStage["AWAITING_APPROVAL"].Should().Be(1);
        result.CompensationsByStage["AWAITING_PAYMENT"].Should().Be(1);
        result.CompensationsByStage["IN_EXECUTION"].Should().Be(2);
        result.CompensationsByStage["COMPLETED"].Should().Be(0);
        result.CompensationsByStage.Values.Sum().Should().Be(4);
    }

    [Fact]
    public void Calculate_EmptyHistory_ReportsZeroForEveryStatus()
    {
        var result = StatusTimelineMetrics.Calculate([], T0);

        result.AverageHoursByStatus.Should().HaveCount(StatusTimelineMetrics.AllStatuses.Count).And.OnlyContain(p => p.Value == 0);
        result.CompensationsByStage.Should().OnlyContain(p => p.Value == 0);
    }
}
