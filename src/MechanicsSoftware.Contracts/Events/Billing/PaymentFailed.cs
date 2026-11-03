// Saga contract published by the Billing Service (ADR-011, F4-16).
// Copied from mechanics-billing-service — the namespace is part of the message identity in
// MassTransit (urn:message:TorqueOS.Contracts.Billing:<Name>) and must match exactly.
// Evolution: add fields only; a breaking change gets a new type (e.g. <Name>V2).
namespace TorqueOS.Contracts.Billing;

/// <summary>The charge expired, was cancelled or failed at Mercado Pago; the order is cancelled.</summary>
public sealed record PaymentFailed(
    Guid ServiceOrderId,
    Guid PaymentId,
    Guid BudgetId,
    string Reason,
    DateTime OccurredAt);
