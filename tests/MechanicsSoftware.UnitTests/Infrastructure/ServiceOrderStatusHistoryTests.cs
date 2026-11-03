using FluentAssertions;
using MechanicsSoftware.Domain.Entities;
using MechanicsSoftware.Domain.ValueObjects;
using MechanicsSoftware.UnitTests.Helpers;
using Microsoft.EntityFrameworkCore;

namespace MechanicsSoftware.UnitTests.Infrastructure;

public class ServiceOrderStatusHistoryTests
{
    [Fact]
    public async Task NewOrder_SavesInitialReceivedHistory()
    {
        await using var db = InMemoryDbContextHelper.Create();
        var order = NewOrder();

        db.ServiceOrders.Add(order);
        await db.SaveChangesAsync();

        var history = await db.ServiceOrderStatusHistory
            .SingleAsync(item => item.ServiceOrderId == order.Id);

        history.Status.Should().Be(ServiceOrderStatus.Status.Received);
        history.EnteredAt.Should().Be(order.CreatedAt);
    }

    [Fact]
    public async Task StatusChange_SavesNewHistoryEntry()
    {
        await using var db = InMemoryDbContextHelper.Create();
        var order = NewOrder();
        db.ServiceOrders.Add(order);
        await db.SaveChangesAsync();

        order.StartDiagnosis();
        await db.SaveChangesAsync();

        var history = await db.ServiceOrderStatusHistory
            .Where(item => item.ServiceOrderId == order.Id)
            .OrderBy(item => item.EnteredAt)
            .ToListAsync();

        history.Should().HaveCount(2);
        history.Select(item => item.Status).Should().ContainInOrder(
            ServiceOrderStatus.Status.Received,
            ServiceOrderStatus.Status.InDiagnosis);
        history[1].EnteredAt.Should().BeOnOrAfter(history[0].EnteredAt);
    }

    [Fact]
    public async Task SaveWithoutStatusChange_DoesNotDuplicateHistory()
    {
        await using var db = InMemoryDbContextHelper.Create();
        var order = NewOrder();
        db.ServiceOrders.Add(order);
        await db.SaveChangesAsync();

        await db.SaveChangesAsync();

        var historyCount = await db.ServiceOrderStatusHistory
            .CountAsync(item => item.ServiceOrderId == order.Id);

        historyCount.Should().Be(1);
    }

    private static ServiceOrder NewOrder() =>
        ServiceOrder.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
}
