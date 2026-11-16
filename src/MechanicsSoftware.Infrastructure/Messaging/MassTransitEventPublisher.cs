using MassTransit;
using MechanicsSoftware.Application.Abstractions;

namespace MechanicsSoftware.Infrastructure.Messaging;

/// <summary>
/// <see cref="IEventPublisher"/> backed by MassTransit. The bus outbox is enabled in
/// <see cref="MessagingSetup"/>, so the scoped <see cref="IPublishEndpoint"/> writes the message
/// to the outbox tables of <c>AppDbContext</c> instead of sending it straight to RabbitMQ.
/// Every saga message handed to the outbox is counted for the saga dashboard (F4-37).
/// </summary>
public sealed class MassTransitEventPublisher(IPublishEndpoint publishEndpoint, ISagaMetrics metrics) : IEventPublisher
{
    public async Task PublishAsync<TEvent>(TEvent integrationEvent, CancellationToken cancellationToken = default)
        where TEvent : class
    {
        await publishEndpoint.Publish(integrationEvent, cancellationToken);
        Count<TEvent>();
    }

    public async Task PublishAsync<TEvent>(TEvent integrationEvent, Guid correlationId, CancellationToken cancellationToken = default)
        where TEvent : class
    {
        await publishEndpoint.Publish(integrationEvent, context => context.CorrelationId = correlationId, cancellationToken);
        Count<TEvent>();
    }

    private void Count<TEvent>()
    {
        if (SagaMetricsObserver.IsSagaMessage<TEvent>())
            metrics.MessagePublished(typeof(TEvent).Name);
    }
}
