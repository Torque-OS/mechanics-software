using FluentAssertions;
using MassTransit;
using MechanicsSoftware.Application.UseCases.ServiceOrders.Handlers;
using MechanicsSoftware.Application.UseCases.ServiceOrders.Handlers.SagaEvents;
using MechanicsSoftware.Domain.Entities;
using MechanicsSoftware.Domain.ValueObjects;
using MechanicsSoftware.Infrastructure.Messaging.Consumers;
using MechanicsSoftware.Infrastructure.Persistence;
using MechanicsSoftware.UnitTests.Helpers;
using Moq;
using TorqueOS.Contracts.Billing;
using TorqueOS.Contracts.Execution;

namespace MechanicsSoftware.UnitTests.Infrastructure;

/// <summary>
/// Each consumer translates its message into the matching saga handler call; the handlers run for
/// real against the in-memory database, so the assertions are on the resulting order status.
/// </summary>
public class SagaEventConsumersTests
{
    private static ConsumeContext<T> Context<T>(T message) where T : class
    {
        var context = new Mock<ConsumeContext<T>>();
        context.SetupGet(c => c.Message).Returns(message);
        context.SetupGet(c => c.CancellationToken).Returns(CancellationToken.None);
        return context.Object;
    }

    private static async Task<ServiceOrder> SeedAsync(AppDbContext db, Action<ServiceOrder> arrange)
    {
        var order = ServiceOrder.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        arrange(order);
        db.ServiceOrders.Add(order);
        await db.SaveChangesAsync();
        return order;
    }

    private static void InDiagnosis(ServiceOrder order)
    {
        order.StartDiagnosis();
        order.AddServiceItem(Guid.NewGuid(), "Oil Change", new Money(5_000), 1);
    }

    private static void AwaitingApproval(ServiceOrder order)
    {
        InDiagnosis(order);
        order.AttachBudget(Guid.NewGuid(), new Money(5_000));
    }

    private static void AwaitingPayment(ServiceOrder order)
    {
        AwaitingApproval(order);
        order.ConfirmBudgetApproval();
    }

    private static void InExecution(ServiceOrder order)
    {
        AwaitingPayment(order);
        order.ConfirmPayment();
    }

    private static async Task<string> StatusOf(AppDbContext db, Guid id)
    {
        db.ChangeTracker.Clear();
        return (await db.ServiceOrders.FindAsync(id))!.Status.ToString();
    }

    [Fact]
    public async Task BudgetGeneratedConsumer_AttachesBudget()
    {
        await using var db = InMemoryDbContextHelper.Create();
        var order = await SeedAsync(db, InDiagnosis);
        var consumer = new BudgetGeneratedConsumer(
            new BudgetGeneratedHandler(db, HandlerStubs.EmailNotifier(), HandlerStubs.Logger<BudgetGeneratedHandler>()));

        await consumer.Consume(Context(new BudgetGenerated(order.Id, Guid.NewGuid(), 5_000, DateTime.UtcNow)));

        (await StatusOf(db, order.Id)).Should().Be("AWAITING_APPROVAL");
    }

    [Fact]
    public async Task BudgetApprovedConsumer_MovesToAwaitingPayment()
    {
        await using var db = InMemoryDbContextHelper.Create();
        var order = await SeedAsync(db, AwaitingApproval);
        var consumer = new BudgetApprovedConsumer(
            new BudgetApprovedHandler(db, HandlerStubs.EmailNotifier(), HandlerStubs.Logger<BudgetApprovedHandler>()));

        await consumer.Consume(Context(new BudgetApproved(order.Id, order.BudgetId!.Value, DateTime.UtcNow)));

        (await StatusOf(db, order.Id)).Should().Be("AWAITING_PAYMENT");
    }

    [Fact]
    public async Task BudgetRejectedConsumer_CancelsOrder()
    {
        await using var db = InMemoryDbContextHelper.Create();
        var order = await SeedAsync(db, AwaitingApproval);
        var consumer = new BudgetRejectedConsumer(
            new BudgetRejectedHandler(db, HandlerStubs.EmailNotifier(), HandlerStubs.Logger<BudgetRejectedHandler>()));

        await consumer.Consume(Context(new BudgetRejected(order.Id, order.BudgetId!.Value, DateTime.UtcNow)));

        (await StatusOf(db, order.Id)).Should().Be("CANCELLED");
    }

    [Fact]
    public async Task BudgetFailedConsumer_CancelsOrderInDiagnosis()
    {
        await using var db = InMemoryDbContextHelper.Create();
        var order = await SeedAsync(db, InDiagnosis);
        var consumer = new BudgetFailedConsumer(
            new BudgetRejectedHandler(db, HandlerStubs.EmailNotifier(), HandlerStubs.Logger<BudgetRejectedHandler>()));

        await consumer.Consume(Context(new BudgetFailed(order.Id, "No service item.", DateTime.UtcNow)));

        (await StatusOf(db, order.Id)).Should().Be("CANCELLED");
    }

    [Fact]
    public async Task PaymentConfirmedConsumer_MovesToInExecution()
    {
        await using var db = InMemoryDbContextHelper.Create();
        var order = await SeedAsync(db, AwaitingPayment);
        var consumer = new PaymentConfirmedConsumer(
            new PaymentConfirmedHandler(db, HandlerStubs.EmailNotifier(), HandlerStubs.Logger<PaymentConfirmedHandler>()));

        await consumer.Consume(Context(new PaymentConfirmed(
            order.Id, Guid.NewGuid(), order.BudgetId!.Value, 5_000, DateTime.UtcNow)));

        (await StatusOf(db, order.Id)).Should().Be("IN_EXECUTION");
    }

    [Fact]
    public async Task PaymentFailedConsumer_CancelsOrder()
    {
        await using var db = InMemoryDbContextHelper.Create();
        var order = await SeedAsync(db, AwaitingPayment);
        var consumer = new PaymentFailedConsumer(
            new PaymentFailedHandler(db, HandlerStubs.EmailNotifier(), HandlerStubs.Logger<PaymentFailedHandler>()));

        await consumer.Consume(Context(new PaymentFailed(
            order.Id, Guid.NewGuid(), order.BudgetId!.Value, "expired", DateTime.UtcNow)));

        (await StatusOf(db, order.Id)).Should().Be("CANCELLED");
    }

    [Fact]
    public async Task ExecutionCompletedConsumer_CompletesOrder()
    {
        await using var db = InMemoryDbContextHelper.Create();
        var order = await SeedAsync(db, InExecution);
        var consumer = new ExecutionCompletedConsumer(new ExecutionCompletedHandler(db,
            new CompleteServiceOrderHandler(db, HandlerStubs.EmailNotifier(), HandlerStubs.Logger<CompleteServiceOrderHandler>())));

        await consumer.Consume(Context(new ExecutionCompleted(order.Id, Guid.NewGuid(), DateTime.UtcNow)));

        (await StatusOf(db, order.Id)).Should().Be("COMPLETED");
    }

    [Fact]
    public async Task ExecutionFailedConsumer_CancelsOrder()
    {
        await using var db = InMemoryDbContextHelper.Create();
        var order = await SeedAsync(db, InExecution);
        var consumer = new ExecutionFailedConsumer(
            new ExecutionFailedHandler(db, HandlerStubs.EmailNotifier(), HandlerStubs.Logger<ExecutionFailedHandler>()));

        await consumer.Consume(Context(new ExecutionFailed(order.Id, Guid.NewGuid(), "Part unavailable.", DateTime.UtcNow)));

        (await StatusOf(db, order.Id)).Should().Be("CANCELLED");
    }
}
