namespace TorqueOS.Contracts.Billing;

public sealed record BudgetFailed(
    Guid ServiceOrderId,
    string Reason,
    DateTime OccurredAt);
