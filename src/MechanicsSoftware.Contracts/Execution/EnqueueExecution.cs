namespace TorqueOS.Contracts.Execution;

public sealed record EnqueueExecution(
    Guid ServiceOrderId,
    int Priority = 0);
