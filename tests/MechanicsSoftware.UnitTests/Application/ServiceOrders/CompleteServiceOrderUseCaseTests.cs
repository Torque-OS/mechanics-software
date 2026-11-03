using FluentAssertions;
using MechanicsSoftware.Application.Abstractions;
using MechanicsSoftware.Application.Exceptions;
using MechanicsSoftware.Application.UseCases.ServiceOrders;
using MechanicsSoftware.Application.UseCases.ServiceOrders.Commands;
using MechanicsSoftware.Application.UseCases.ServiceOrders.Handlers;
using MechanicsSoftware.Application.UseCases.ServiceOrders.Queries;
using MechanicsSoftware.UnitTests.Helpers;
using Moq;
using MechanicsSoftware.Domain.Entities;
using MechanicsSoftware.Domain.ValueObjects;
using MechanicsSoftware.Domain.Enums;
using MechanicsSoftware.Domain.Exceptions;

namespace MechanicsSoftware.UnitTests.Application.ServiceOrders;

public class CompleteServiceOrderUseCaseTests
{
    private static ServiceOrder BuildOrderInExecution()
    {
        var order = ServiceOrder.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        order.StartDiagnosis();
        order.AddServiceItem(Guid.NewGuid(), "Oil Change", new Money(5000), 1);
        order.AttachBudget(Guid.NewGuid(), new Money(5000));
        order.ConfirmBudgetApproval();
        order.ConfirmPayment();
        return order;
    }

    [Fact]
    public async Task ExecuteAsync_NoPartItems_StatusBecomesCompleted()
    {
        await using var db = InMemoryDbContextHelper.Create();
        var order = BuildOrderInExecution();
        db.ServiceOrders.Add(order);
        await db.SaveChangesAsync();

        var result = await new CompleteServiceOrderHandler(db, HandlerStubs.EmailNotifier(), HandlerStubs.Logger<CompleteServiceOrderHandler>()).ExecuteAsync(order.Id);

        result.Status.Should().Be("COMPLETED");
    }

    [Fact]
    public async Task ExecuteAsync_WithAvailableParts_ConfirmsUsage()
    {
        // EF Core InMemory cannot INSERT new OwnsMany entries (StockMovement) onto an
        // already-saved Part when SaveChanges is called a second time. Use Moq here.
        var part = Part.Create(Guid.NewGuid(), "FILTER-001", "Oil Filter", null, new Money(2000), 5);

        var order = ServiceOrder.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        order.StartDiagnosis();
        order.AddServiceItem(Guid.NewGuid(), "Oil Change", new Money(5000), 1);
        part.Reserve(2, order.Id);
        order.AddPartItem(part.Id, part.Name, part.UnitPrice, 2, PartAvailability.Available);
        order.AttachBudget(Guid.NewGuid(), new Money(5000));
        order.ConfirmBudgetApproval();
        order.ConfirmPayment();

        var mockOrders = MockDbSetHelper.CreateMockDbSet([order]);
        var mockParts = MockDbSetHelper.CreateMockDbSet([part]);
        mockParts.Setup(m => m.FindAsync(It.IsAny<object[]>(), It.IsAny<CancellationToken>()))
                 .Returns(new ValueTask<Part?>(part));

        var db = new Mock<IAppDbContext>();
        db.Setup(d => d.ServiceOrders).Returns(mockOrders.Object);
        db.Setup(d => d.Parts).Returns(mockParts.Object);
        db.Setup(d => d.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var result = await new CompleteServiceOrderHandler(db.Object, HandlerStubs.EmailNotifier(), HandlerStubs.Logger<CompleteServiceOrderHandler>()).ExecuteAsync(order.Id);

        result.Status.Should().Be("COMPLETED");
        part.ReservedQuantity.Should().Be(0);
    }

    [Fact]
    public async Task ExecuteAsync_OrderNotFound_ThrowsNotFoundException()
    {
        await using var db = InMemoryDbContextHelper.Create();

        var act = async () => await new CompleteServiceOrderHandler(db, HandlerStubs.EmailNotifier(), HandlerStubs.Logger<CompleteServiceOrderHandler>()).ExecuteAsync(Guid.NewGuid());

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task ExecuteAsync_WrongStatus_ThrowsDomainException()
    {
        await using var db = InMemoryDbContextHelper.Create();
        var order = ServiceOrder.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        db.ServiceOrders.Add(order);
        await db.SaveChangesAsync();

        var act = async () => await new CompleteServiceOrderHandler(db, HandlerStubs.EmailNotifier(), HandlerStubs.Logger<CompleteServiceOrderHandler>()).ExecuteAsync(order.Id);

        await act.Should().ThrowAsync<DomainException>();
    }
}
