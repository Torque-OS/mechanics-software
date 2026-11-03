namespace TorqueOS.Contracts.Execution;

public sealed record ExecutionFailed(
    Guid ServiceOrderId,
    Guid ExecutionOrderId,
    string Reason,
    DateTime FailedAt);
