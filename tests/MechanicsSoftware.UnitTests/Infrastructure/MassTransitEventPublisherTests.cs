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
}
