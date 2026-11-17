using MassTransit;
using MechanicsSoftware.Application.Abstractions;
using MechanicsSoftware.Infrastructure.Messaging;
using Moq;
using TorqueOS.Contracts.Billing;
using TorqueOS.Contracts.Execution;

namespace MechanicsSoftware.UnitTests.Infrastructure;

public class SagaMetricsObserverTests
{
    private readonly Mock<ISagaMetrics> metrics = new();
    private SagaMetricsObserver Observer => new(metrics.Object);

    [Fact]
    public async Task PostConsume_SagaContract_CountsByMessageType()
    {
        await Observer.PostConsume(Mock.Of<ConsumeContext<PaymentConfirmed>>());

        metrics.Verify(m => m.MessageConsumed(nameof(PaymentConfirmed)), Times.Once);
    }

    [Fact]
    public async Task ConsumeFault_SagaContract_CountsFault()
    {
        await Observer.ConsumeFault(Mock.Of<ConsumeContext<ExecutionFailed>>(), new InvalidOperationException());

        metrics.Verify(m => m.ConsumeFaulted(nameof(ExecutionFailed)), Times.Once);
    }

    [Fact]
    public async Task EventPublisher_SagaCommand_CountsPublish()
    {
        var publisher = new MassTransitEventPublisher(Mock.Of<IPublishEndpoint>(), metrics.Object);

        await publisher.PublishAsync(new CancelBudget(Guid.NewGuid()), Guid.NewGuid());

        metrics.Verify(m => m.MessagePublished(nameof(CancelBudget)), Times.Once);
    }

    [Fact]
    public async Task NonContractMessages_AreIgnored()
    {
        await Observer.PostConsume(Mock.Of<ConsumeContext<Fault<PaymentConfirmed>>>());
        await Observer.ConsumeFault(Mock.Of<ConsumeContext<Fault<PaymentConfirmed>>>(), new InvalidOperationException());

        metrics.VerifyNoOtherCalls();
    }
}
