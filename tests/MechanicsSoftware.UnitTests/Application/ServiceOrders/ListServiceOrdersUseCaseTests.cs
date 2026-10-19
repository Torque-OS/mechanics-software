using FluentAssertions;
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

public class ListServiceOrdersUseCaseTests
{
    [Fact]
    public async Task ExecuteAsync_NoFilter_ReturnsActiveOrders()
    {
        await using var db = InMemoryDbContextHelper.Create();
        db.ServiceOrders.Add(CreateOrderInState(ServiceOrderStatus.Status.Received));
        db.ServiceOrders.Add(CreateOrderInState(ServiceOrderStatus.Status.Received));
        await db.SaveChangesAsync();

        var result = await new ListServiceOrdersHandler(db).ExecuteAsync(new ListServiceOrdersQuery());

        result.Should().HaveCount(2);
    }

    [Fact]
    public async Task ExecuteAsync_StatusFilter_ReturnsMatchingOrders()
    {
        await using var db = InMemoryDbContextHelper.Create();
        db.ServiceOrders.Add(CreateOrderInState(ServiceOrderStatus.Status.Received));
        db.ServiceOrders.Add(CreateOrderInState(ServiceOrderStatus.Status.InDiagnosis));
        await db.SaveChangesAsync();

        var result = await new ListServiceOrdersHandler(db).ExecuteAsync(
            new ListServiceOrdersQuery(Status: "RECEIVED"));

        result.Should().HaveCount(1);
        result[0].Status.Should().Be("RECEIVED");
    }

    [Fact]
    public async Task ExecuteAsync_InvalidStatusFilter_ReturnsActiveOrders()
    {
        await using var db = InMemoryDbContextHelper.Create();
        db.ServiceOrders.Add(CreateOrderInState(ServiceOrderStatus.Status.Received));
        await db.SaveChangesAsync();

        var result = await new ListServiceOrdersHandler(db).ExecuteAsync(
            new ListServiceOrdersQuery(Status: "UNKNOWN_STATUS"));

        result.Should().HaveCount(1);
    }

    [Fact]
    public async Task ExecuteAsync_NoFilter_ExcludesCompletedAndDeliveredOrders()
    {
        await using var db = InMemoryDbContextHelper.Create();
        db.ServiceOrders.Add(CreateOrderInState(ServiceOrderStatus.Status.Received));
        db.ServiceOrders.Add(CreateOrderInState(ServiceOrderStatus.Status.InDiagnosis));
        db.ServiceOrders.Add(CreateOrderInState(ServiceOrderStatus.Status.Completed));
        db.ServiceOrders.Add(CreateOrderInState(ServiceOrderStatus.Status.Delivered));
        await db.SaveChangesAsync();

        var result = await new ListServiceOrdersHandler(db).ExecuteAsync(new ListServiceOrdersQuery());

        result.Should().HaveCount(2);
        result.Should().NotContain(o => o.Status == "COMPLETED" || o.Status == "DELIVERED");
    }

    [Fact]
    public async Task ExecuteAsync_NoFilter_OrdersByStatusPriorityThenCreatedAt()
    {
        await using var db = InMemoryDbContextHelper.Create();
        db.ServiceOrders.Add(CreateOrderInState(ServiceOrderStatus.Status.Received));
        db.ServiceOrders.Add(CreateOrderInState(ServiceOrderStatus.Status.InDiagnosis));
        db.ServiceOrders.Add(CreateOrderInState(ServiceOrderStatus.Status.AwaitingApproval));
        db.ServiceOrders.Add(CreateOrderInState(ServiceOrderStatus.Status.InExecution));
        await db.SaveChangesAsync();

        var result = await new ListServiceOrdersHandler(db).ExecuteAsync(new ListServiceOrdersQuery());

        result.Should().HaveCount(4);
        result[0].Status.Should().Be("IN_EXECUTION");
        result[1].Status.Should().Be("AWAITING_APPROVAL");
        result[2].Status.Should().Be("IN_DIAGNOSIS");
        result[3].Status.Should().Be("RECEIVED");
    }

    private static ServiceOrder CreateOrderInState(ServiceOrderStatus.Status target)
    {
        var order = ServiceOrder.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        if (target == ServiceOrderStatus.Status.Received) return order;
        order.StartDiagnosis();
        if (target == ServiceOrderStatus.Status.InDiagnosis) return order;
        order.AddServiceItem(Guid.NewGuid(), "Service", new Money(1_000), 1);
        order.AttachBudget(Guid.NewGuid(), new Money(1_000));
        if (target == ServiceOrderStatus.Status.AwaitingApproval) return order;
        order.ConfirmBudgetApproval();
        if (target == ServiceOrderStatus.Status.AwaitingPayment) return order;
        order.ConfirmPayment();
        if (target == ServiceOrderStatus.Status.InExecution) return order;
        order.Complete();
        if (target == ServiceOrderStatus.Status.Completed) return order;
        order.Deliver();
        return order;
    }
}
