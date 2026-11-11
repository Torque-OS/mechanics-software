using FluentAssertions;
using MassTransit;
using MechanicsSoftware.Infrastructure.Messaging;
using Moq;
using TorqueOS.Contracts.ServiceOrders;

namespace MechanicsSoftware.UnitTests.Infrastructure;

public class MassTransitEventPublisherTests
{
    [Fact]
    public async Task PublishAsync_ForwardsEventAndCancellationTokenToMassTransit()
    {
        var endpoint = new Mock<IPublishEndpoint>();
        var publisher = new MassTransitEventPublisher(endpoint.Object);
        var integrationEvent = new ServiceOrderOpened(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), DateTime.UtcNow);
        using var cts = new CancellationTokenSource();

        await publisher.PublishAsync(integrationEvent, cts.Token);

        endpoint.Verify(e => e.Publish(integrationEvent, cts.Token), Times.Once);
    }

    [Fact]
    public async Task PublishAsync_WithCorrelationId_SetsItOnTheMessage()
    {
        var endpoint = new Mock<IPublishEndpoint>();
        IPipe<PublishContext<ServiceOrderOpened>>? pipe = null;
        endpoint
            .Setup(e => e.Publish(
                It.IsAny<ServiceOrderOpened>(), It.IsAny<IPipe<PublishContext<ServiceOrderOpened>>>(), It.IsAny<CancellationToken>()))
            .Callback<ServiceOrderOpened, IPipe<PublishContext<ServiceOrderOpened>>, CancellationToken>((_, p, _) => pipe = p)
            .Returns(Task.CompletedTask);
        var orderId = Guid.NewGuid();
        var integrationEvent = new ServiceOrderOpened(orderId, Guid.NewGuid(), Guid.NewGuid(), DateTime.UtcNow);

        await new MassTransitEventPublisher(endpoint.Object).PublishAsync(integrationEvent, orderId);

        var context = new Mock<PublishContext<ServiceOrderOpened>>();
        context.SetupProperty(c => c.CorrelationId);
        await pipe!.Send(context.Object);
        context.Object.CorrelationId.Should().Be(orderId);
    }
}
