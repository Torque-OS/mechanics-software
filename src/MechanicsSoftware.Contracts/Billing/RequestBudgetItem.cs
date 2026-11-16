namespace TorqueOS.Contracts.Billing;

public sealed record RequestBudgetItem(
    string Type,
    Guid ReferenceId,
    string Description,
    int UnitPriceInCents,
    int Quantity);
