using FluentAssertions;
using MechanicsSoftware.Application.Abstractions;
using MechanicsSoftware.Application.Exceptions;
using MechanicsSoftware.Application.UseCases.ServiceOrders.Handlers;
using MechanicsSoftware.Application.UseCases.ServiceOrders.Handlers.SagaEvents;
using MechanicsSoftware.Domain.Entities;
using MechanicsSoftware.Domain.Enums;
using MechanicsSoftware.Domain.Exceptions;
using MechanicsSoftware.Domain.ValueObjects;
using MechanicsSoftware.UnitTests.Helpers;
using Moq;
using TorqueOS.Contracts.Execution;
using TorqueOS.Contracts.Billing;
using MechanicsSoftware.Infrastructure.Persistence;

namespace MechanicsSoftware.UnitTests.Application.ServiceOrders.SagaEvents;

public class SagaEventHandlersTests
{
    private static ServiceOrder OrderAwaitingApproval()
    {
        var order = ServiceOrder.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        order.StartDiagnosis();
        order.AddServiceItem(Guid.NewGuid(), "Oil Change", new Money(5_000), 1);
        order.AttachBudget(Guid.NewGuid(), new Money(5_000));
        return order;
    }

    private static ServiceOrder OrderAwaitingPayment()
    {
        var order = OrderAwaitingApproval();
        order.ConfirmBudgetApproval();
        return order;
    }

    [Fact]
    public async Task BudgetGeneratedHandler_ValidOrder_AttachesBudgetAndMovesToAwaitingApproval()
    {
        await using var db = InMemoryDbContextHelper.Create();
        var order = ServiceOrder.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        order.StartDiagnosis();
        db.ServiceOrders.Add(order);
        await db.SaveChangesAsync();

        var budgetId = Guid.NewGuid();
        var result = await new BudgetGeneratedHandler(db, HandlerStubs.EmailNotifier(), HandlerStubs.Logger<BudgetGeneratedHandler>())
            .ExecuteAsync(order.Id, budgetId, 5_000);

        result.Status.Should().Be("AWAITING_APPROVAL");
        result.BudgetId.Should().Be(budgetId);
        result.BudgetTotalInCents.Should().Be(5_000);
    }

    [Fact]
    public async Task BudgetGeneratedHandler_OrderNotFound_ThrowsNotFoundException()
    {
        await using var db = InMemoryDbContextHelper.Create();

        var act = async () => await new BudgetGeneratedHandler(db, HandlerStubs.EmailNotifier(), HandlerStubs.Logger<BudgetGeneratedHandler>())
            .ExecuteAsync(Guid.NewGuid(), Guid.NewGuid(), 5_000);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task BudgetApprovedHandler_FromAwaitingApproval_MovesToAwaitingPayment()
    {
        await using var db = InMemoryDbContextHelper.Create();
        var order = OrderAwaitingApproval();
        db.ServiceOrders.Add(order);
        await db.SaveChangesAsync();

        var result = await new BudgetApprovedHandler(db, HandlerStubs.EmailNotifier(), HandlerStubs.Logger<BudgetApprovedHandler>())
            .ExecuteAsync(order.Id);

        result.Status.Should().Be("AWAITING_PAYMENT");
    }

    [Fact]
    public async Task BudgetApprovedHandler_WrongStatus_ThrowsDomainException()
    {
        await using var db = InMemoryDbContextHelper.Create();
        var order = ServiceOrder.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        db.ServiceOrders.Add(order);
        await db.SaveChangesAsync();

        var act = async () => await new BudgetApprovedHandler(db, HandlerStubs.EmailNotifier(), HandlerStubs.Logger<BudgetApprovedHandler>())
            .ExecuteAsync(order.Id);

        await act.Should().ThrowAsync<DomainException>();
    }

    [Fact]
    public async Task PaymentConfirmedHandler_FromAwaitingPayment_MovesToInExecution()
    {
        await using var db = InMemoryDbContextHelper.Create();
        var order = OrderAwaitingPayment();
        db.ServiceOrders.Add(order);
        await db.SaveChangesAsync();

        var result = await new PaymentConfirmedHandler(db, HandlerStubs.EventPublisher(), HandlerStubs.EmailNotifier(), HandlerStubs.Logger<PaymentConfirmedHandler>())
            .ExecuteAsync(order.Id);

        result.Status.Should().Be("IN_EXECUTION");
    }

    [Fact]
    public async Task BudgetRejectedHandler_ValidOrder_CancelsAndReleasesParts()
    {
        // EF Core InMemory can't insert a new OwnsMany entry (StockMovement) onto an
        // already-saved parent on a second SaveChanges call — use Moq, as the other
        // ServiceOrders handler tests do for the same reason.
        var part = Part.Create(Guid.NewGuid(), "FILTER-001", "Oil Filter", null, new Money(2000), 10);
        var order = ServiceOrder.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        order.StartDiagnosis();
        order.AddServiceItem(Guid.NewGuid(), "Oil Change", new Money(5_000), 1);
        part.Reserve(2, order.Id);
        order.AddPartItem(part.Id, part.Name, part.UnitPrice, 2, PartAvailability.Available);
        order.AttachBudget(Guid.NewGuid(), new Money(9_000));

        var mockOrders = MockDbSetHelper.CreateMockDbSet([order]);
        var mockParts = MockDbSetHelper.CreateMockDbSet([part]);
        mockParts.Setup(m => m.FindAsync(It.IsAny<object[]>(), It.IsAny<CancellationToken>()))
                 .Returns(new ValueTask<Part?>(part));

        var db = new Mock<IAppDbContext>();
        db.Setup(d => d.ServiceOrders).Returns(mockOrders.Object);
        db.Setup(d => d.Parts).Returns(mockParts.Object);
        db.Setup(d => d.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var result = await new BudgetRejectedHandler(db.Object, HandlerStubs.EmailNotifier(), HandlerStubs.Logger<BudgetRejectedHandler>())
            .ExecuteAsync(order.Id);

        result.Status.Should().Be("CANCELLED");
        part.AvailableQuantity.Should().Be(10);
    }

    [Fact]
    public async Task PaymentFailedHandler_ValidOrder_Cancels()
    {
        await using var db = InMemoryDbContextHelper.Create();
        var order = OrderAwaitingPayment();
        db.ServiceOrders.Add(order);
        await db.SaveChangesAsync();

        var result = await new PaymentFailedHandler(db, HandlerStubs.EventPublisher(), HandlerStubs.EmailNotifier(), HandlerStubs.Logger<PaymentFailedHandler>())
            .ExecuteAsync(order.Id);

        result.Status.Should().Be("CANCELLED");
    }

    [Fact]
    public async Task ExecutionFailedHandler_ValidOrder_Cancels()
    {
        await using var db = InMemoryDbContextHelper.Create();
        var order = OrderAwaitingPayment();
        order.ConfirmPayment();
        db.ServiceOrders.Add(order);
        await db.SaveChangesAsync();

        var result = await new ExecutionFailedHandler(db, HandlerStubs.EventPublisher(), HandlerStubs.EmailNotifier(), HandlerStubs.Logger<ExecutionFailedHandler>())
            .ExecuteAsync(order.Id);

        result.Status.Should().Be("CANCELLED");
    }

    private static ServiceOrder OrderInExecution()
    {
        var order = OrderAwaitingPayment();
        order.ConfirmPayment();
        return order;
    }

    [Fact]
    public async Task BudgetGeneratedHandler_SameBudgetTwice_IsIdempotent()
    {
        await using var db = InMemoryDbContextHelper.Create();
        var order = OrderAwaitingApproval();
        db.ServiceOrders.Add(order);
        await db.SaveChangesAsync();

        var result = await new BudgetGeneratedHandler(db, HandlerStubs.EmailNotifier(), HandlerStubs.Logger<BudgetGeneratedHandler>())
            .ExecuteAsync(order.Id, order.BudgetId!.Value, 5_000);

        result.Status.Should().Be("AWAITING_APPROVAL");
        result.BudgetId.Should().Be(order.BudgetId);
    }

    [Fact]
    public async Task BudgetApprovedHandler_AlreadyAwaitingPayment_IsIdempotent()
    {
        await using var db = InMemoryDbContextHelper.Create();
        var order = OrderAwaitingPayment();
        db.ServiceOrders.Add(order);
        await db.SaveChangesAsync();

        var result = await new BudgetApprovedHandler(db, HandlerStubs.EmailNotifier(), HandlerStubs.Logger<BudgetApprovedHandler>())
            .ExecuteAsync(order.Id);

        result.Status.Should().Be("AWAITING_PAYMENT");
    }

    [Fact]
    public async Task PaymentConfirmedHandler_AlreadyInExecution_IsIdempotent()
    {
        await using var db = InMemoryDbContextHelper.Create();
        var order = OrderInExecution();
        db.ServiceOrders.Add(order);
        await db.SaveChangesAsync();

        var result = await new PaymentConfirmedHandler(db, HandlerStubs.EventPublisher(), HandlerStubs.EmailNotifier(), HandlerStubs.Logger<PaymentConfirmedHandler>())
            .ExecuteAsync(order.Id);

        result.Status.Should().Be("IN_EXECUTION");
    }

    [Fact]
    public async Task BudgetRejectedHandler_FromInDiagnosis_Cancels()
    {
        await using var db = InMemoryDbContextHelper.Create();
        var order = ServiceOrder.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        order.StartDiagnosis();
        db.ServiceOrders.Add(order);
        await db.SaveChangesAsync();

        var result = await new BudgetRejectedHandler(db, HandlerStubs.EmailNotifier(), HandlerStubs.Logger<BudgetRejectedHandler>())
            .ExecuteAsync(order.Id);

        result.Status.Should().Be("CANCELLED");
    }

    [Fact]
    public async Task BudgetRejectedHandler_AlreadyCancelled_IsIdempotent()
    {
        await using var db = InMemoryDbContextHelper.Create();
        var order = OrderAwaitingApproval();
        order.Cancel();
        db.ServiceOrders.Add(order);
        await db.SaveChangesAsync();

        var result = await new BudgetRejectedHandler(db, HandlerStubs.EmailNotifier(), HandlerStubs.Logger<BudgetRejectedHandler>())
            .ExecuteAsync(order.Id);

        result.Status.Should().Be("CANCELLED");
    }

    [Fact]
    public async Task PaymentFailedHandler_AlreadyCancelled_IsIdempotent()
    {
        await using var db = InMemoryDbContextHelper.Create();
        var order = OrderAwaitingPayment();
        order.Cancel();
        db.ServiceOrders.Add(order);
        await db.SaveChangesAsync();

        var result = await new PaymentFailedHandler(db, HandlerStubs.EventPublisher(), HandlerStubs.EmailNotifier(), HandlerStubs.Logger<PaymentFailedHandler>())
            .ExecuteAsync(order.Id);

        result.Status.Should().Be("CANCELLED");
    }

    [Fact]
    public async Task ExecutionFailedHandler_AlreadyCancelled_IsIdempotent()
    {
        await using var db = InMemoryDbContextHelper.Create();
        var order = OrderInExecution();
        order.Cancel();
        db.ServiceOrders.Add(order);
        await db.SaveChangesAsync();

        var result = await new ExecutionFailedHandler(db, HandlerStubs.EventPublisher(), HandlerStubs.EmailNotifier(), HandlerStubs.Logger<ExecutionFailedHandler>())
            .ExecuteAsync(order.Id);

        result.Status.Should().Be("CANCELLED");
    }

    [Fact]
    public async Task ExecutionCompletedHandler_FromInExecution_Completes()
    {
        await using var db = InMemoryDbContextHelper.Create();
        var order = OrderInExecution();
        db.ServiceOrders.Add(order);
        await db.SaveChangesAsync();

        var result = await NewExecutionCompletedHandler(db).ExecuteAsync(order.Id);

        result.Status.Should().Be("COMPLETED");
    }

    [Fact]
    public async Task ExecutionCompletedHandler_AlreadyCompleted_IsIdempotent()
    {
        await using var db = InMemoryDbContextHelper.Create();
        var order = OrderInExecution();
        order.Complete();
        db.ServiceOrders.Add(order);
        await db.SaveChangesAsync();

        var result = await NewExecutionCompletedHandler(db).ExecuteAsync(order.Id);

        result.Status.Should().Be("COMPLETED");
    }

    private static ExecutionCompletedHandler NewExecutionCompletedHandler(IAppDbContext db) =>
        new(db, new CompleteServiceOrderHandler(
            db, HandlerStubs.EmailNotifier(), HandlerStubs.Logger<CompleteServiceOrderHandler>()));

    private static async Task<(AppDbContext Db, string Name, ServiceOrder Order)> SeedAsync(ServiceOrder order)
    {
        var name = Guid.NewGuid().ToString();
        var db = InMemoryDbContextHelper.Create(name);
        db.ServiceOrders.Add(order);
        await db.SaveChangesAsync();
        return (db, name, order);
    }

    private static Mock<IEventPublisher> PublisherCapturingCommittedStatus<TCommand>(
        string databaseName, Guid orderId, Action<bool> onPublish, ServiceOrderStatus.Status committedBefore)
        where TCommand : class
    {
        var publisher = new Mock<IEventPublisher>();
        publisher
            .Setup(p => p.PublishAsync(It.IsAny<TCommand>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Callback(() =>
            {
                using var other = InMemoryDbContextHelper.Create(databaseName);
                onPublish(other.ServiceOrders.Single(o => o.Id == orderId).Status.Is(committedBefore));
            })
            .Returns(Task.CompletedTask);
        return publisher;
    }

    [Fact]
    public async Task PaymentConfirmedHandler_PublishesEnqueueExecutionCorrelatedToTheOrder_BeforeSaving()
    {
        var (db, name, order) = await SeedAsync(OrderAwaitingPayment());
        await using var _ = db;
        var publishedBeforeCommit = false;
        var publisher = PublisherCapturingCommittedStatus<EnqueueExecution>(
            name, order.Id, still => publishedBeforeCommit = still, ServiceOrderStatus.Status.AwaitingPayment);

        await new PaymentConfirmedHandler(db, publisher.Object, HandlerStubs.EmailNotifier(), HandlerStubs.Logger<PaymentConfirmedHandler>())
            .ExecuteAsync(order.Id);

        publisher.Verify(p => p.PublishAsync(
                It.Is<EnqueueExecution>(c => c.ServiceOrderId == order.Id && c.Priority == 0),
                order.Id,
                It.IsAny<CancellationToken>()),
            Times.Once);
        publishedBeforeCommit.Should().BeTrue();
    }

    [Fact]
    public async Task PaymentConfirmedHandler_AlreadyInExecution_DoesNotEnqueueAgain()
    {
        var (db, _, order) = await SeedAsync(OrderInExecution());
        await using var __ = db;
        var publisher = new Mock<IEventPublisher>();

        await new PaymentConfirmedHandler(db, publisher.Object, HandlerStubs.EmailNotifier(), HandlerStubs.Logger<PaymentConfirmedHandler>())
            .ExecuteAsync(order.Id);

        publisher.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task PaymentFailedHandler_PublishesCancelBudgetCorrelatedToTheOrder_BeforeSaving()
    {
        var (db, name, order) = await SeedAsync(OrderAwaitingPayment());
        await using var _ = db;
        var publishedBeforeCommit = false;
        var publisher = PublisherCapturingCommittedStatus<CancelBudget>(
            name, order.Id, still => publishedBeforeCommit = still, ServiceOrderStatus.Status.AwaitingPayment);

        await new PaymentFailedHandler(db, publisher.Object, HandlerStubs.EmailNotifier(), HandlerStubs.Logger<PaymentFailedHandler>())
            .ExecuteAsync(order.Id);

        publisher.Verify(p => p.PublishAsync(
                It.Is<CancelBudget>(c => c.ServiceOrderId == order.Id), order.Id, It.IsAny<CancellationToken>()),
            Times.Once);
        publishedBeforeCommit.Should().BeTrue();
    }

    [Fact]
    public async Task ExecutionFailedHandler_PublishesCancelBudgetCorrelatedToTheOrder_BeforeSaving()
    {
        var (db, name, order) = await SeedAsync(OrderInExecution());
        await using var _ = db;
        var publishedBeforeCommit = false;
        var publisher = PublisherCapturingCommittedStatus<CancelBudget>(
            name, order.Id, still => publishedBeforeCommit = still, ServiceOrderStatus.Status.InExecution);

        await new ExecutionFailedHandler(db, publisher.Object, HandlerStubs.EmailNotifier(), HandlerStubs.Logger<ExecutionFailedHandler>())
            .ExecuteAsync(order.Id);

        publisher.Verify(p => p.PublishAsync(
                It.Is<CancelBudget>(c => c.ServiceOrderId == order.Id), order.Id, It.IsAny<CancellationToken>()),
            Times.Once);
        publishedBeforeCommit.Should().BeTrue();
    }

    [Fact]
    public async Task PaymentFailedHandler_AlreadyCancelled_DoesNotCancelBudgetAgain()
    {
        var cancelled = OrderAwaitingPayment();
        cancelled.Cancel();
        var (db, _, order) = await SeedAsync(cancelled);
        await using var __ = db;
        var publisher = new Mock<IEventPublisher>();

        await new PaymentFailedHandler(db, publisher.Object, HandlerStubs.EmailNotifier(), HandlerStubs.Logger<PaymentFailedHandler>())
            .ExecuteAsync(order.Id);

        publisher.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ExecutionFailedHandler_AlreadyCancelled_DoesNotCancelBudgetAgain()
    {
        var cancelled = OrderInExecution();
        cancelled.Cancel();
        var (db, _, order) = await SeedAsync(cancelled);
        await using var __ = db;
        var publisher = new Mock<IEventPublisher>();

        await new ExecutionFailedHandler(db, publisher.Object, HandlerStubs.EmailNotifier(), HandlerStubs.Logger<ExecutionFailedHandler>())
            .ExecuteAsync(order.Id);

        publisher.VerifyNoOtherCalls();
    }
}
