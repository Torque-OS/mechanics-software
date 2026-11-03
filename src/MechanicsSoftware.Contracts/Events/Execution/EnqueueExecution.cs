// Saga command consumed by the Execution Service (ADR-011, F4-18).
// Copied from mechanics-execution-service — the Execution Service owns this contract; the
// namespace is part of the message identity in MassTransit
// (urn:message:TorqueOS.Contracts.Execution:<Name>) and must match exactly.
// Evolution: add fields only; a breaking change gets a new type (e.g. <Name>V2).
namespace TorqueOS.Contracts.Execution;

/// <summary>
/// Command to the Execution Service: payment was confirmed, so enqueue the order for
/// diagnosis/repair. <paramref name="Priority"/> is optional (default 0, plain FIFO) — the OS
/// Service does not send a priority hint yet. Idempotent on the Execution Service's side.
/// </summary>
public sealed record EnqueueExecution(Guid ServiceOrderId, int Priority = 0);
