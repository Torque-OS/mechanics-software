namespace TorqueOS.Contracts.Billing;

public sealed record PaymentFailed(
    Guid ServiceOrderId,
    Guid PaymentId,
    Guid BudgetId,
    string Reason,
    DateTime OccurredAt);
