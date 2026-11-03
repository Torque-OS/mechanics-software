using FluentAssertions;
using MechanicsSoftware.Application.Exceptions;
using MechanicsSoftware.Application.UseCases.ServiceOrders;
using MechanicsSoftware.Application.UseCases.ServiceOrders.Commands;
using MechanicsSoftware.Application.UseCases.ServiceOrders.Handlers;
using MechanicsSoftware.Application.UseCases.ServiceOrders.Queries;
using MechanicsSoftware.UnitTests.Helpers;
using MechanicsSoftware.Domain.Entities;
using MechanicsSoftware.Domain.ValueObjects;
using MechanicsSoftware.Domain.Enums;
using MechanicsSoftware.Domain.Exceptions;

namespace MechanicsSoftware.UnitTests.Application.ServiceOrders;

public class StartExecutionUseCaseTests
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
    public async Task ExecuteAsync_OrderInExecution_ReturnsResponse()
    {
        await using var db = InMemoryDbContextHelper.Create();
        var order = BuildOrderInExecution();
        db.ServiceOrders.Add(order);
        await db.SaveChangesAsync();

        var result = await new StartExecutionHandler(db, HandlerStubs.EmailNotifier(), HandlerStubs.Logger<StartExecutionHandler>()).ExecuteAsync(order.Id);

        result.Status.Should().Be("IN_EXECUTION");
    }

    [Fact]
    public async Task ExecuteAsync_OrderNotFound_ThrowsNotFoundException()
    {
        await using var db = InMemoryDbContextHelper.Create();

        var act = async () => await new StartExecutionHandler(db, HandlerStubs.EmailNotifier(), HandlerStubs.Logger<StartExecutionHandler>()).ExecuteAsync(Guid.NewGuid());

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task ExecuteAsync_WrongStatus_ThrowsDomainException()
    {
        await using var db = InMemoryDbContextHelper.Create();
        var order = ServiceOrder.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        db.ServiceOrders.Add(order);
        await db.SaveChangesAsync();

        var act = async () => await new StartExecutionHandler(db, HandlerStubs.EmailNotifier(), HandlerStubs.Logger<StartExecutionHandler>()).ExecuteAsync(order.Id);

        await act.Should().ThrowAsync<DomainException>();
    }
}
