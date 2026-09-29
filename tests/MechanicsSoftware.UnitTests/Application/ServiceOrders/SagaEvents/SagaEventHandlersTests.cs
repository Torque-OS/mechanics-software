using FluentAssertions;
using MechanicsSoftware.Application.Abstractions;
using MechanicsSoftware.Application.Exceptions;
using MechanicsSoftware.Application.UseCases.ServiceOrders.Handlers.SagaEvents;
using MechanicsSoftware.Domain.Entities;
using MechanicsSoftware.Domain.Enums;
using MechanicsSoftware.Domain.Exceptions;
using MechanicsSoftware.Domain.ValueObjects;
using MechanicsSoftware.UnitTests.Helpers;
using Moq;

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

        var result = await new PaymentConfirmedHandler(db, HandlerStubs.EmailNotifier(), HandlerStubs.Logger<PaymentConfirmedHandler>())
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

        var result = await new PaymentFailedHandler(db, HandlerStubs.EmailNotifier(), HandlerStubs.Logger<PaymentFailedHandler>())
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

        var result = await new ExecutionFailedHandler(db, HandlerStubs.EmailNotifier(), HandlerStubs.Logger<ExecutionFailedHandler>())
            .ExecuteAsync(order.Id);

        result.Status.Should().Be("CANCELLED");
    }
}
