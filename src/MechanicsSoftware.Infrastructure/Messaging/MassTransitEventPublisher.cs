using MassTransit;
using MechanicsSoftware.Application.Abstractions;

namespace MechanicsSoftware.Infrastructure.Messaging;

/// <summary>
/// <see cref="IEventPublisher"/> backed by MassTransit. The bus outbox is enabled in
/// <see cref="MessagingSetup"/>, so the scoped <see cref="IPublishEndpoint"/> writes the message
/// to the outbox tables of <c>AppDbContext</c> instead of sending it straight to RabbitMQ.
/// </summary>
public sealed class MassTransitEventPublisher(IPublishEndpoint publishEndpoint) : IEventPublisher
{
    public Task PublishAsync<TEvent>(
        TEvent integrationEvent, Guid? correlationId = null, CancellationToken cancellationToken = default)
        where TEvent : class =>
        correlationId is { } id
            ? publishEndpoint.Publish(integrationEvent, context => context.CorrelationId = id, cancellationToken)
            : publishEndpoint.Publish(integrationEvent, cancellationToken);
}
