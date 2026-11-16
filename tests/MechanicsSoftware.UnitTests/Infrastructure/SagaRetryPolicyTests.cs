using FluentAssertions;
using MassTransit;
using MassTransit.Testing;
using MechanicsSoftware.Application.Exceptions;
using MechanicsSoftware.Domain.Exceptions;
using MechanicsSoftware.Infrastructure.Messaging;
using Microsoft.Extensions.DependencyInjection;

namespace MechanicsSoftware.UnitTests.Infrastructure;

public sealed class SagaRetryPolicyTests : IAsyncLifetime
{
    private readonly AttemptCounter _attempts = new();
    private ServiceProvider _provider = null!;
    private ITestHarness _harness = null!;

    public async Task InitializeAsync()
    {
        _provider = new ServiceCollection()
            .AddSingleton(_attempts)
            .AddMassTransitTestHarness(bus =>
            {
                bus.AddConsumer<ProbeConsumer>();
                bus.AddConfigureEndpointsCallback((_, _, endpoint) => endpoint.UseMessageRetry(SagaRetryPolicy.Configure));
            })
            .AddOptions<TestHarnessOptions>().Configure(options =>
            {
                options.TestTimeout = TimeSpan.FromSeconds(30);
                options.TestInactivityTimeout = TimeSpan.FromSeconds(5);
            }).Services
            .BuildServiceProvider(true);
        _harness = _provider.GetRequiredService<ITestHarness>();
        await _harness.Start();
    }

    public async Task DisposeAsync() => await _provider.DisposeAsync();

    [Fact]
    public async Task TransientFailure_IsRetriedUntilItSucceeds()
    {
        var probe = new RetryProbe(Guid.NewGuid(), FailuresBeforeSuccess: 2, Failure: "transient");

        await _harness.Bus.Publish(probe);

        (await _harness.Consumed.Any<RetryProbe>(m => m.Context.Message.Id == probe.Id && m.Exception is null))
            .Should().BeTrue();
        (await _harness.Published.Any<Fault<RetryProbe>>()).Should().BeFalse();
        _attempts.Of(probe.Id).Should().Be(3);
    }

    [Theory]
    [InlineData("domain")]
    [InlineData("conflict")]
    [InlineData("not-found")]
    public async Task PermanentFailure_GoesToTheErrorQueueWithoutRetrying(string failure)
    {
        var probe = new RetryProbe(Guid.NewGuid(), FailuresBeforeSuccess: int.MaxValue, Failure: failure);

        await _harness.Bus.Publish(probe);

        (await _harness.Published.Any<Fault<RetryProbe>>(m => m.Context.Message.Message.Id == probe.Id)).Should().BeTrue();
        _attempts.Of(probe.Id).Should().Be(1);
    }

    public sealed record RetryProbe(Guid Id, int FailuresBeforeSuccess, string Failure);

    public sealed class AttemptCounter
    {
        private readonly Dictionary<Guid, int> _attempts = [];

        public int Increment(Guid id)
        {
            lock (_attempts)
                return _attempts[id] = _attempts.GetValueOrDefault(id) + 1;
        }

        public int Of(Guid id)
        {
            lock (_attempts)
                return _attempts.GetValueOrDefault(id);
        }
    }

    public sealed class ProbeConsumer(AttemptCounter attempts) : IConsumer<RetryProbe>
    {
        public Task Consume(ConsumeContext<RetryProbe> context)
        {
            var probe = context.Message;
            if (attempts.Increment(probe.Id) <= probe.FailuresBeforeSuccess)
            {
                throw probe.Failure switch
                {
                    "domain" => new DomainException("Business rule violated."),
                    "conflict" => new ConflictException("Invalid status transition."),
                    "not-found" => new NotFoundException("ServiceOrder", probe.Id),
                    _ => new InvalidOperationException("Transient failure."),
                };
            }

            return Task.CompletedTask;
        }
    }
}
