namespace TorqueOS.Contracts.Billing;

public sealed record BudgetApproved(
    Guid ServiceOrderId,
    Guid BudgetId,
    DateTime OccurredAt);
