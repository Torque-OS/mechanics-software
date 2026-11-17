using MechanicsSoftware.Domain.ValueObjects;

namespace MechanicsSoftware.API.Metrics;

public sealed record StatusHistoryEntry(Guid ServiceOrderId, ServiceOrderStatus.Status Status, DateTime EnteredAt);

/// <summary>
/// Per-stage figures for the saga dashboard (F4-37), computed from <c>service_order_status_history</c>.
/// Pure so it can be unit tested; <see cref="ServiceOrderMetricsRefreshService"/> feeds it from the database.
/// </summary>
public sealed class StatusTimelineMetrics
{
    /// <summary>Every status, as exported in the <c>status</c>/<c>stage</c> label.</summary>
    public static IReadOnlyList<string> AllStatuses { get; } =
        Enum.GetValues<ServiceOrderStatus.Status>().Select(Label).ToList();

    /// <summary>Average hours an order spent in each status. The current status counts until <c>now</c>.</summary>
    public IReadOnlyDictionary<string, double> AverageHoursByStatus { get; }

    /// <summary>Cancelled orders by the status they were in right before <c>CANCELLED</c>.</summary>
    public IReadOnlyDictionary<string, long> CompensationsByStage { get; }

    private StatusTimelineMetrics(
        IReadOnlyDictionary<string, double> averageHoursByStatus,
        IReadOnlyDictionary<string, long> compensationsByStage)
    {
        AverageHoursByStatus = averageHoursByStatus;
        CompensationsByStage = compensationsByStage;
    }

    public static StatusTimelineMetrics Calculate(IEnumerable<StatusHistoryEntry> history, DateTime now)
    {
        var durations = AllStatuses.ToDictionary(status => status, _ => new List<double>());
        var compensations = AllStatuses.ToDictionary(status => status, _ => 0L);

        foreach (var timeline in history.GroupBy(entry => entry.ServiceOrderId))
        {
            var entries = timeline.OrderBy(entry => entry.EnteredAt).ToList();

            for (var index = 0; index < entries.Count; index++)
            {
                var leftAt = index + 1 < entries.Count ? entries[index + 1].EnteredAt : now;
                durations[Label(entries[index].Status)].Add((leftAt - entries[index].EnteredAt).TotalHours);

                if (index > 0 && entries[index].Status == ServiceOrderStatus.Status.Cancelled)
                    compensations[Label(entries[index - 1].Status)]++;
            }
        }

        return new StatusTimelineMetrics(
            durations.ToDictionary(pair => pair.Key, pair => pair.Value.Count == 0 ? 0 : Math.Round(pair.Value.Average(), 2)),
            compensations);
    }

    private static string Label(ServiceOrderStatus.Status status) => new ServiceOrderStatus(status).ToString();
}
