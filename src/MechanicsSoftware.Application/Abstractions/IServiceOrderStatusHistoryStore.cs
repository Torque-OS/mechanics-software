namespace MechanicsSoftware.Application.Abstractions;

public sealed record ServiceOrderStatusHistoryRecord(
    Guid Id,
    Guid ServiceOrderId,
    string Status,
    DateTime EnteredAt);

public interface IServiceOrderStatusHistoryStore
{
    Task AppendAsync(
        IReadOnlyCollection<ServiceOrderStatusHistoryRecord> records,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ServiceOrderStatusHistoryRecord>> GetAsync(
        Guid serviceOrderId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ServiceOrderStatusHistoryRecord>> GetAsync(
        IReadOnlyCollection<Guid> serviceOrderIds,
        CancellationToken cancellationToken = default);
}
