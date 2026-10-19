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

public class DeliverServiceOrderUseCaseTests
{
    private static ServiceOrder BuildOrderCompleted()
    {
        var order = ServiceOrder.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        order.StartDiagnosis();
        order.AddServiceItem(Guid.NewGuid(), "Oil Change", new Money(5000), 1);
        order.AttachBudget(Guid.NewGuid(), new Money(5000));
        order.ConfirmBudgetApproval();
        order.ConfirmPayment();
        order.Complete();
        return order;
    }

    [Fact]
    public async Task ExecuteAsync_CompletedOrder_StatusBecomesDelivered()
    {
        await using var db = InMemoryDbContextHelper.Create();
        var order = BuildOrderCompleted();
        db.ServiceOrders.Add(order);
        await db.SaveChangesAsync();

        var result = await new DeliverServiceOrderHandler(db, HandlerStubs.EmailNotifier(), HandlerStubs.Logger<DeliverServiceOrderHandler>()).ExecuteAsync(order.Id);

        result.Status.Should().Be("DELIVERED");
    }

    [Fact]
    public async Task ExecuteAsync_OrderNotFound_ThrowsNotFoundException()
    {
        await using var db = InMemoryDbContextHelper.Create();

        var act = async () => await new DeliverServiceOrderHandler(db, HandlerStubs.EmailNotifier(), HandlerStubs.Logger<DeliverServiceOrderHandler>()).ExecuteAsync(Guid.NewGuid());

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task ExecuteAsync_WrongStatus_ThrowsDomainException()
    {
        await using var db = InMemoryDbContextHelper.Create();
        var order = ServiceOrder.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        db.ServiceOrders.Add(order);
        await db.SaveChangesAsync();

        var act = async () => await new DeliverServiceOrderHandler(db, HandlerStubs.EmailNotifier(), HandlerStubs.Logger<DeliverServiceOrderHandler>()).ExecuteAsync(order.Id);

        await act.Should().ThrowAsync<DomainException>();
    }
}
