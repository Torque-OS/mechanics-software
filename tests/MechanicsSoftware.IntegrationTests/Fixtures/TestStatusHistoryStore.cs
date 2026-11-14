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
        GetAsync([serviceOrderId], cancellationToken);

    public Task<IReadOnlyList<ServiceOrderStatusHistoryRecord>> GetAsync(
        IReadOnlyCollection<Guid> serviceOrderIds,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ServiceOrderStatusHistoryRecord>>(
            _records
                .Where(record => serviceOrderIds.Contains(record.ServiceOrderId))
                .OrderBy(record => record.EnteredAt)
                .ToList());

    public void Clear() => _records.Clear();
}
