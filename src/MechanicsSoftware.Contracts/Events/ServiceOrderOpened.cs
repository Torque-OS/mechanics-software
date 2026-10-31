namespace MechanicsSoftware.Contracts.Events;

/// <summary>
/// Integration event published by the OS Service when a new service order is opened
/// (status RECEIVED). It starts the service order saga (ADR-011).
/// </summary>
/// <param name="ServiceOrderId">Saga correlation key; every downstream message carries it.</param>
/// <param name="CustomerId">Customer who owns the service order.</param>
/// <param name="VehicleId">Vehicle the service order refers to.</param>
/// <param name="OpenedAt">When the order was opened, in UTC.</param>
public sealed record ServiceOrderOpened(
    Guid ServiceOrderId,
    Guid CustomerId,
    Guid VehicleId,
    DateTime OpenedAt);
