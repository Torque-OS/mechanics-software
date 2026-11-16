using MassTransit;
using MechanicsSoftware.Application.Abstractions;

namespace MechanicsSoftware.Infrastructure.Messaging;

/// <summary>
/// Counts the saga messages (the <c>TorqueOS.Contracts</c> types) the OS Service consumes, and the
/// consumes that fault, for the saga dashboard (F4-37). Other messages, such as MassTransit's own
/// <c>Fault&lt;T&gt;</c>, are ignored. Publishes are counted in <see cref="MassTransitEventPublisher"/>,
/// the one place the OS Service publishes from: messages published inside a consumer go through the
/// endpoint outbox and never reach a bus-level publish observer.
/// </summary>
public sealed class SagaMetricsObserver(ISagaMetrics metrics) : IConsumeObserver
{
    private const string ContractsNamespace = "TorqueOS.Contracts";

    public Task PreConsume<T>(ConsumeContext<T> context) where T : class => Task.CompletedTask;

    public Task PostConsume<T>(ConsumeContext<T> context) where T : class
    {
        if (IsSagaMessage<T>())
            metrics.MessageConsumed(typeof(T).Name);

        return Task.CompletedTask;
    }

    public Task ConsumeFault<T>(ConsumeContext<T> context, Exception exception) where T : class
    {
        if (IsSagaMessage<T>())
            metrics.ConsumeFaulted(typeof(T).Name);

        return Task.CompletedTask;
    }

    internal static bool IsSagaMessage<T>() =>
        typeof(T).Namespace?.StartsWith(ContractsNamespace, StringComparison.Ordinal) == true;
}
