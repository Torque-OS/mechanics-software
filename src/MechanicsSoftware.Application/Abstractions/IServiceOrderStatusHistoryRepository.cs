using MechanicsSoftware.Domain.ValueObjects;

namespace MechanicsSoftware.Application.Abstractions;

public sealed record ServiceOrderStatusHistoryEntry(
    Guid ServiceOrderId,
    ServiceOrderStatus.Status Status,
    DateTime EnteredAt);

public interface IServiceOrderStatusHistoryRepository
{
    Task AppendAsync(
        Guid serviceOrderId,
        ServiceOrderStatus.Status status,
        DateTime enteredAt,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ServiceOrderStatusHistoryEntry>> GetAllAsync(
        CancellationToken cancellationToken = default);
}