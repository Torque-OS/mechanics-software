namespace TorqueOS.Contracts.Billing;

public sealed record BudgetGenerated(
    Guid ServiceOrderId,
    Guid BudgetId,
    int TotalInCents,
    DateTime OccurredAt);
