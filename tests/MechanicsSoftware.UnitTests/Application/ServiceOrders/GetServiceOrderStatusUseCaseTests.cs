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

public class GetServiceOrderStatusUseCaseTests
{
    [Fact]
    public async Task ExecuteAsync_ExistingOrder_ReturnsStatus()
    {
        await using var db = InMemoryDbContextHelper.Create();
        var order = ServiceOrder.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        db.ServiceOrders.Add(order);
        await db.SaveChangesAsync();

        var result = await new GetServiceOrderStatusHandler(db).ExecuteAsync(order.Id);

        result.Id.Should().Be(order.Id);
        result.Status.Should().Be("RECEIVED");
        result.DeliveredAt.Should().BeNull();
    }

    [Fact]
    public async Task ExecuteAsync_OrderNotFound_ThrowsNotFoundException()
    {
        await using var db = InMemoryDbContextHelper.Create();

        var act = async () => await new GetServiceOrderStatusHandler(db).ExecuteAsync(Guid.NewGuid());

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task ExecuteAsync_OwnerCustomer_ReturnsOrder()
    {
        await using var db = InMemoryDbContextHelper.Create();
        var customerId = Guid.NewGuid();
        var order = ServiceOrder.Create(Guid.NewGuid(), customerId, Guid.NewGuid());
        db.ServiceOrders.Add(order);
        await db.SaveChangesAsync();

        var result = await new GetServiceOrderStatusHandler(db).ExecuteAsync(order.Id, customerId);

        result.Id.Should().Be(order.Id);
    }

    [Fact]
    public async Task ExecuteAsync_AnotherCustomersOrder_ThrowsNotFoundException()
    {
        await using var db = InMemoryDbContextHelper.Create();
        var order = ServiceOrder.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        db.ServiceOrders.Add(order);
        await db.SaveChangesAsync();

        var act = async () => await new GetServiceOrderStatusHandler(db).ExecuteAsync(order.Id, Guid.NewGuid());

        await act.Should().ThrowAsync<NotFoundException>();
    }
}
