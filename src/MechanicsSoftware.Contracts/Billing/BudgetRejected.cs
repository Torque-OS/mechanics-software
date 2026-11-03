namespace TorqueOS.Contracts.Billing;

public sealed record BudgetRejected(
    Guid ServiceOrderId,
    Guid BudgetId,
    DateTime OccurredAt);
