using FluentAssertions;
using MassTransit;
using MassTransit.Testing;
using MechanicsSoftware.Application.Abstractions;
using MechanicsSoftware.Application.UseCases.ServiceOrders.Handlers.SagaEvents;
using MechanicsSoftware.Domain.Entities;
using MechanicsSoftware.Domain.ValueObjects;
using MechanicsSoftware.Infrastructure.Messaging;
using MechanicsSoftware.Infrastructure.Persistence;
using MechanicsSoftware.UnitTests.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TorqueOS.Contracts.Billing;
using TorqueOS.Contracts.Execution;

namespace MechanicsSoftware.UnitTests.Infrastructure;

/// <summary>
/// The Saga's happy path (ADR-011, F4-22) and compensation path (F4-23) end to end in memory:
/// each Billing/Execution event in, the order's status (and any outward compensation command)
/// updated. Retry, inbox and outbox (the <c>*ConsumerDefinition</c> classes) need PostgreSQL and
/// RabbitMQ and are checked against the compose stack.
/// </summary>
public sealed class SagaConsumersTests : IAsyncLifetime
{
    private readonly string _databaseName = Guid.NewGuid().ToString();
    private ServiceProvider _provider = null!;
    private ITestHarness _harness = null!;

    public async Task InitializeAsync()
    {
        _provider = new ServiceCollection()
            .AddDbContext<AppDbContext>(options => options.UseInMemoryDatabase(_databaseName))
            .AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>())
            .AddSingleton<IEmailNotifier>(HandlerStubs.EmailNotifier())
            .AddScoped<IEventPublisher, MassTransitEventPublisher>()
            .AddScoped<BudgetGeneratedHandler>()
            .AddScoped<BudgetApprovedHandler>()
            .AddScoped<PaymentConfirmedHandler>()
            .AddScoped<ExecutionCompletedHandler>()
            .AddScoped<BudgetFailedHandler>()
            .AddScoped<BudgetRejectedHandler>()
            .AddScoped<PaymentFailedHandler>()
            .AddScoped<ExecutionFailedHandler>()
            .AddMassTransitTestHarness(bus =>
            {
                bus.AddConsumer<BudgetGeneratedConsumer>();
                bus.AddConsumer<BudgetApprovedConsumer>();
                bus.AddConsumer<PaymentConfirmedConsumer>();
                bus.AddConsumer<ExecutionCompletedConsumer>();
                bus.AddConsumer<BudgetFailedConsumer>();
                bus.AddConsumer<BudgetRejectedConsumer>();
                bus.AddConsumer<PaymentFailedConsumer>();
                bus.AddConsumer<ExecutionFailedConsumer>();
            })
            .BuildServiceProvider(true);
        _harness = _provider.GetRequiredService<ITestHarness>();
        await _harness.Start();
    }

    public async Task DisposeAsync() => await _provider.DisposeAsync();

    private async Task<ServiceOrder> SeedOrderAsync(Action<ServiceOrder> advanceTo)
    {
        await using var scope = _provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var order = ServiceOrder.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        advanceTo(order);
        db.ServiceOrders.Add(order);
        await db.SaveChangesAsync();
        return order;
    }

    [Fact]
    public async Task BudgetGenerated_AttachesBudgetAndMovesToAwaitingApproval()
    {
        var order = await SeedOrderAsync(o => o.StartDiagnosis());
        var budgetId = Guid.NewGuid();

        await _harness.Bus.Publish(new BudgetGenerated(order.Id, budgetId, 5_000, DateTime.UtcNow));

        (await _harness.Consumed.Any<BudgetGenerated>()).Should().BeTrue();

        await using var scope = _provider.CreateAsyncScope();
        var stored = await scope.ServiceProvider.GetRequiredService<AppDbContext>().ServiceOrders.SingleAsync();
        stored.Status.Value.Should().Be(ServiceOrderStatus.Status.AwaitingApproval);
        stored.BudgetId.Should().Be(budgetId);
    }

    [Fact]
    public async Task BudgetApproved_MovesToAwaitingPayment()
    {
        var order = await SeedOrderAsync(o =>
        {
            o.StartDiagnosis();
            o.AttachBudget(Guid.NewGuid(), new Money(5_000));
        });

        await _harness.Bus.Publish(new BudgetApproved(order.Id, order.BudgetId!.Value, DateTime.UtcNow));

        (await _harness.Consumed.Any<BudgetApproved>()).Should().BeTrue();

        await using var scope = _provider.CreateAsyncScope();
        var stored = await scope.ServiceProvider.GetRequiredService<AppDbContext>().ServiceOrders.SingleAsync();
        stored.Status.Value.Should().Be(ServiceOrderStatus.Status.AwaitingPayment);
    }

    [Fact]
    public async Task PaymentConfirmed_MovesToInExecutionAndPublishesEnqueueExecution()
    {
        var order = await SeedOrderAsync(o =>
        {
            o.StartDiagnosis();
            o.AttachBudget(Guid.NewGuid(), new Money(5_000));
            o.ConfirmBudgetApproval();
        });

        await _harness.Bus.Publish(new PaymentConfirmed(order.Id, Guid.NewGuid(), order.BudgetId!.Value, 5_000, DateTime.UtcNow));

        (await _harness.Consumed.Any<PaymentConfirmed>()).Should().BeTrue();
        var published = _harness.Published.Select<EnqueueExecution>().Single().Context;
        published.Message.ServiceOrderId.Should().Be(order.Id);
        published.CorrelationId.Should().Be(order.Id);

        await using var scope = _provider.CreateAsyncScope();
        var stored = await scope.ServiceProvider.GetRequiredService<AppDbContext>().ServiceOrders.SingleAsync();
        stored.Status.Value.Should().Be(ServiceOrderStatus.Status.InExecution);
    }

    [Fact]
    public async Task ExecutionCompleted_MovesToCompleted()
    {
        var order = await SeedOrderAsync(o =>
        {
            o.StartDiagnosis();
            o.AttachBudget(Guid.NewGuid(), new Money(5_000));
            o.ConfirmBudgetApproval();
            o.ConfirmPayment();
        });

        await _harness.Bus.Publish(new ExecutionCompleted(order.Id, Guid.NewGuid(), DateTime.UtcNow));

        (await _harness.Consumed.Any<ExecutionCompleted>()).Should().BeTrue();

        await using var scope = _provider.CreateAsyncScope();
        var stored = await scope.ServiceProvider.GetRequiredService<AppDbContext>().ServiceOrders.SingleAsync();
        stored.Status.Value.Should().Be(ServiceOrderStatus.Status.Completed);
    }

    [Fact]
    public async Task BudgetFailed_CancelsFromInDiagnosis()
    {
        var order = await SeedOrderAsync(o => o.StartDiagnosis());

        await _harness.Bus.Publish(new BudgetFailed(order.Id, "No service item", DateTime.UtcNow));

        (await _harness.Consumed.Any<BudgetFailed>()).Should().BeTrue();

        await using var scope = _provider.CreateAsyncScope();
        var stored = await scope.ServiceProvider.GetRequiredService<AppDbContext>().ServiceOrders.SingleAsync();
        stored.Status.Value.Should().Be(ServiceOrderStatus.Status.Cancelled);
    }

    [Fact]
    public async Task BudgetRejected_CancelsFromAwaitingApproval()
    {
        var order = await SeedOrderAsync(o =>
        {
            o.StartDiagnosis();
            o.AttachBudget(Guid.NewGuid(), new Money(5_000));
        });

        await _harness.Bus.Publish(new BudgetRejected(order.Id, order.BudgetId!.Value, DateTime.UtcNow));

        (await _harness.Consumed.Any<BudgetRejected>()).Should().BeTrue();

        await using var scope = _provider.CreateAsyncScope();
        var stored = await scope.ServiceProvider.GetRequiredService<AppDbContext>().ServiceOrders.SingleAsync();
        stored.Status.Value.Should().Be(ServiceOrderStatus.Status.Cancelled);
    }

    [Fact]
    public async Task PaymentFailed_CancelsAndPublishesCancelBudget()
    {
        var order = await SeedOrderAsync(o =>
        {
            o.StartDiagnosis();
            o.AttachBudget(Guid.NewGuid(), new Money(5_000));
            o.ConfirmBudgetApproval();
        });

        await _harness.Bus.Publish(new PaymentFailed(order.Id, Guid.NewGuid(), order.BudgetId!.Value, "Card declined", DateTime.UtcNow));

        (await _harness.Consumed.Any<PaymentFailed>()).Should().BeTrue();
        var published = _harness.Published.Select<CancelBudget>().Single().Context;
        published.Message.ServiceOrderId.Should().Be(order.Id);
        published.CorrelationId.Should().Be(order.Id);

        await using var scope = _provider.CreateAsyncScope();
        var stored = await scope.ServiceProvider.GetRequiredService<AppDbContext>().ServiceOrders.SingleAsync();
        stored.Status.Value.Should().Be(ServiceOrderStatus.Status.Cancelled);
    }

    [Fact]
    public async Task ExecutionFailed_CancelsAndPublishesCancelBudget()
    {
        var order = await SeedOrderAsync(o =>
        {
            o.StartDiagnosis();
            o.AttachBudget(Guid.NewGuid(), new Money(5_000));
            o.ConfirmBudgetApproval();
            o.ConfirmPayment();
        });

        await _harness.Bus.Publish(new ExecutionFailed(order.Id, Guid.NewGuid(), "Part unavailable", DateTime.UtcNow));

        (await _harness.Consumed.Any<ExecutionFailed>()).Should().BeTrue();
        var published = _harness.Published.Select<CancelBudget>().Single().Context;
        published.Message.ServiceOrderId.Should().Be(order.Id);

        await using var scope = _provider.CreateAsyncScope();
        var stored = await scope.ServiceProvider.GetRequiredService<AppDbContext>().ServiceOrders.SingleAsync();
        stored.Status.Value.Should().Be(ServiceOrderStatus.Status.Cancelled);
    }
}
