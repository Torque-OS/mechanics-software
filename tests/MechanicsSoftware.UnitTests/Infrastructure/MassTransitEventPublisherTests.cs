using FluentAssertions;
using MassTransit;
using MechanicsSoftware.Infrastructure.Messaging;
using Moq;
using TorqueOS.Contracts.ServiceOrders;

namespace MechanicsSoftware.UnitTests.Infrastructure;

public class MassTransitEventPublisherTests
{
    [Fact]
    public async Task PublishAsync_NoCorrelationId_ForwardsEventAndCancellationTokenToMassTransit()
    {
        var endpoint = new Mock<IPublishEndpoint>();
        var publisher = new MassTransitEventPublisher(endpoint.Object);
        var integrationEvent = new ServiceOrderOpened(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), DateTime.UtcNow);
        using var cts = new CancellationTokenSource();

        await publisher.PublishAsync(integrationEvent, cancellationToken: cts.Token);

        endpoint.Verify(e => e.Publish(integrationEvent, cts.Token), Times.Once);
    }

    // Publish(message, Action<PublishContext<T>>, token) is a MassTransit extension method, not a
    // real interface member, so Moq can't verify the call shape directly. The CorrelationId it
    // sets is covered end to end by SagaConsumersTests, which reads it back off a real
    // ConsumeContext/PublishContext through the in-memory test harness.
    [Fact]
    public async Task PublishAsync_WithCorrelationId_DoesNotThrow()
    {
        var endpoint = new Mock<IPublishEndpoint>();
        endpoint
            .Setup(e => e.Publish(It.IsAny<ServiceOrderOpened>(), It.IsAny<IPipe<PublishContext<ServiceOrderOpened>>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var publisher = new MassTransitEventPublisher(endpoint.Object);
        var integrationEvent = new ServiceOrderOpened(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), DateTime.UtcNow);

        var act = async () => await publisher.PublishAsync(integrationEvent, Guid.NewGuid());

        await act.Should().NotThrowAsync();
    }
}
