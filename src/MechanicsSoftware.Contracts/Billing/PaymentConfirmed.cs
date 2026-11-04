namespace TorqueOS.Contracts.Billing;

public sealed record PaymentConfirmed(
    Guid ServiceOrderId,
    Guid PaymentId,
    Guid BudgetId,
    int AmountInCents,
    DateTime OccurredAt);
