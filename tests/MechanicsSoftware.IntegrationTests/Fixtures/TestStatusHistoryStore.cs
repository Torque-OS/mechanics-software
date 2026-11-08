using MechanicsSoftware.Application.Abstractions;

namespace MechanicsSoftware.IntegrationTests.Fixtures;

public sealed class TestStatusHistoryStore : IServiceOrderStatusHistoryStore
{
    private readonly List<ServiceOrderStatusHistoryRecord> _records = [];

    public Task AppendAsync(
        IReadOnlyCollection<ServiceOrderStatusHistoryRecord> records,
        CancellationToken cancellationToken = default)
    {
        foreach (var record in records)
        {
            if (_records.Any(existing =>
                existing.ServiceOrderId == record.ServiceOrderId &&
                existing.Status == record.Status &&
                existing.EnteredAt == record.EnteredAt))
                continue;

            _records.Add(record);
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ServiceOrderStatusHistoryRecord>> GetAsync(
        Guid serviceOrderId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ServiceOrderStatusHistoryRecord>>(
            _records
                .Where(record => record.ServiceOrderId == serviceOrderId)
                .OrderBy(record => record.EnteredAt)
                .ToList());

    public void Clear() => _records.Clear();
}
