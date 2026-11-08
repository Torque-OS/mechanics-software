using FluentAssertions;
using MechanicsSoftware.Application.Exceptions;
using MechanicsSoftware.Application.UseCases.ServiceOrders.Handlers;
using MechanicsSoftware.Domain.Entities;
using MechanicsSoftware.Domain.ValueObjects;
using MechanicsSoftware.UnitTests.Helpers;

namespace MechanicsSoftware.UnitTests.Application.ServiceOrders;

public class GetServiceOrderHistoryHandlerTests
{
    [Fact]
    public async Task ExecuteAsync_OrderWithStatusChanges_ReturnsTimelineOldestFirst()
    {
        await using var db = InMemoryDbContextHelper.Create();
        var order = ServiceOrder.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        db.ServiceOrders.Add(order);
        await db.SaveChangesAsync();

        order.StartDiagnosis();
        await db.SaveChangesAsync();

        order.AddServiceItem(Guid.NewGuid(), "Oil Change", new Money(5_000), 1);
        order.AttachBudget(Guid.NewGuid(), new Money(5_000));
        await db.SaveChangesAsync();

        var result = await new GetServiceOrderHistoryHandler(
            db,
            InMemoryDbContextHelper.GetHistoryStore(db)).ExecuteAsync(order.Id);

        result.Id.Should().Be(order.Id);
        result.CurrentStatus.Should().Be("AWAITING_APPROVAL");
        result.History.Select(entry => entry.Status).Should()
            .Equal("RECEIVED", "IN_DIAGNOSIS", "AWAITING_APPROVAL");
        result.History.Select(entry => entry.EnteredAt).Should().BeInAscendingOrder();
    }

    [Fact]
    public async Task ExecuteAsync_OrderNotFound_ThrowsNotFoundException()
    {
        await using var db = InMemoryDbContextHelper.Create();

        var act = async () => await new GetServiceOrderHistoryHandler(
            db,
            InMemoryDbContextHelper.GetHistoryStore(db)).ExecuteAsync(Guid.NewGuid());

        await act.Should().ThrowAsync<NotFoundException>();
    }
}
