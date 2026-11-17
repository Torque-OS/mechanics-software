namespace TorqueOS.Contracts.Billing;

public sealed record RequestBudget(
    Guid ServiceOrderId,
    Guid CustomerId,
    IReadOnlyList<RequestBudgetItem> Items);
