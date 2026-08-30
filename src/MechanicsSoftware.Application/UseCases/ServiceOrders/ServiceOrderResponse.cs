using MechanicsSoftware.Domain.Entities;
using MechanicsSoftware.Domain.Enums;

namespace MechanicsSoftware.Application.UseCases.ServiceOrders;

public sealed record ServiceItemResponse(
    Guid Id,
    Guid ServiceId,
    string ServiceName,
    int UnitPriceInCents,
    int Quantity,
    int TotalInCents)
{
    public static ServiceItemResponse From(ServiceItem item) =>
        new(item.Id, item.ServiceId, item.ServiceName,
            item.UnitPrice.Cents, item.Quantity, item.Total.Cents);
}

public sealed record PartItemResponse(
    Guid Id,
    Guid PartId,
    string PartName,
    int UnitPriceInCents,
    int Quantity,
    string Availability,
    int TotalInCents)
{
    public static PartItemResponse From(PartItem item) =>
        new(item.Id, item.PartId, item.PartName,
            item.UnitPrice.Cents, item.Quantity,
            item.Availability == PartAvailability.Available ? "AVAILABLE" : "UNAVAILABLE",
            item.Total.Cents);
}

public sealed record BudgetResponse(
    Guid Id,
    int TotalInCents,
    string TotalFormatted,
    string Status,
    DateTime CreatedAt)
{
    public static BudgetResponse From(Budget budget) =>
        new(budget.Id, budget.Total.Cents, budget.Total.ToFormatted(),
            budget.Status.ToString(), budget.CreatedAt);
}

public sealed record ServiceOrderResponse(
    Guid Id,
    Guid CustomerId,
    Guid VehicleId,
    string Status,
    DateTime CreatedAt,
    DateTime? CompletedAt,
    DateTime? DeliveredAt,
    BudgetResponse? Budget,
    IReadOnlyList<ServiceItemResponse> ServiceItems,
    IReadOnlyList<PartItemResponse> PartItems)
{
    public static ServiceOrderResponse From(ServiceOrder order) =>
        new(order.Id,
            order.CustomerId,
            order.VehicleId,
            order.Status.ToString(),
            order.CreatedAt,
            order.CompletedAt,
            order.DeliveredAt,
            order.Budget is not null ? BudgetResponse.From(order.Budget) : null,
            order.ServiceItems.Select(ServiceItemResponse.From).ToList(),
            order.PartItems.Select(PartItemResponse.From).ToList());
}

public sealed record ServiceOrderSummaryResponse(
    Guid Id,
    Guid CustomerId,
    Guid VehicleId,
    string Status,
    DateTime CreatedAt
);

public sealed record ServiceOrderStatusResponse(
    Guid Id,
    string Status,
    DateTime CreatedAt,
    DateTime? DeliveredAt
);

public sealed record AddPartItemResponse(
    Guid Id,
    Guid PartId,
    string PartName,
    int UnitPriceInCents,
    int Quantity,
    string Availability,
    int TotalInCents,
    string? Warning);

public sealed record AverageExecutionTimeResponse(
    double AverageHours,
    int OrderCount
);

public sealed record DailyServiceOrderVolumeResponse(
    DateTime Date,
    int Opened,
    int Closed
);
