// Saga command consumed by the Billing Service (ADR-011, F4-17).
// Copied from mechanics-billing-service — the Billing Service owns this contract; the
// namespace is part of the message identity in MassTransit
// (urn:message:TorqueOS.Contracts.Billing:<Name>) and must match exactly.
// Evolution: add fields only; a breaking change gets a new type (e.g. <Name>V2).
namespace TorqueOS.Contracts.Billing;

/// <summary>
/// Compensation command from the OS Service: the order was cancelled, so cancel its budget and
/// settle the payment — an open QR is cancelled, a received payment is refunded. Sent only while
/// the order may still be cancelled (never for a completed order). Idempotent on the Billing
/// Service's side, also when repeated.
/// </summary>
public sealed record CancelBudget(Guid ServiceOrderId);
