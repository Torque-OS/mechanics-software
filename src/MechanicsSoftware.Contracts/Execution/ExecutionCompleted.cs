namespace TorqueOS.Contracts.Execution;

public sealed record ExecutionCompleted(
    Guid ServiceOrderId,
    Guid ExecutionOrderId,
    DateTime CompletedAt);
