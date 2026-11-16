namespace MechanicsSoftware.Application.Abstractions;

public interface IBillingClient
{
    Task<BillingBudget?> GetBudgetByServiceOrderAsync(Guid serviceOrderId, CancellationToken cancellationToken = default);

    Task<BillingPayment?> GetPaymentByBudgetAsync(Guid budgetId, CancellationToken cancellationToken = default);
}

public sealed record BillingBudget(
    Guid Id,
    string Status,
    int TotalInCents,
    DateTime CreatedAt,
    DateTime? DecidedAt,
    IReadOnlyList<BillingBudgetItem> Items);

public sealed record BillingBudgetItem(
    string Type,
    Guid ReferenceId,
    string Description,
    int UnitPriceInCents,
    int Quantity,
    int TotalInCents);

public sealed record BillingPayment(
    Guid Id,
    string Status,
    int AmountInCents,
    string? QrData,
    string? FailureReason,
    DateTime CreatedAt,
    DateTime? ConfirmedAt,
    DateTime? RefundedAt);
