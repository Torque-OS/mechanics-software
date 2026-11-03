// Saga event published by the Execution Service (ADR-011, F4-20).
// Copied from mechanics-execution-service — the namespace is part of the message identity in
// MassTransit (urn:message:TorqueOS.Contracts.Execution:<Name>) and must match exactly.
// Evolution: add fields only; a breaking change gets a new type (e.g. <Name>V2).
namespace TorqueOS.Contracts.Execution;

/// <summary>
/// The repair could not be finished — triggers the Saga compensation path (ADR-011): the OS
/// Service cancels the order and commands the Billing Service to refund the payment and cancel
/// the budget.
/// </summary>
public sealed record ExecutionFailed(Guid ServiceOrderId, Guid ExecutionOrderId, string Reason, DateTime FailedAt);
