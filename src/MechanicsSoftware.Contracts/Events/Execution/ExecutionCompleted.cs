// Saga event published by the Execution Service (ADR-011, F4-20).
// Copied from mechanics-execution-service — the namespace is part of the message identity in
// MassTransit (urn:message:TorqueOS.Contracts.Execution:<Name>) and must match exactly.
// Evolution: add fields only; a breaking change gets a new type (e.g. <Name>V2).
namespace TorqueOS.Contracts.Execution;

/// <summary>The repair finished successfully; the OS Service's Saga can mark the order COMPLETED.</summary>
public sealed record ExecutionCompleted(Guid ServiceOrderId, Guid ExecutionOrderId, DateTime CompletedAt);
