using FluentAssertions;
using MechanicsSoftware.Application.Abstractions;
using MechanicsSoftware.Application.UseCases.ServiceOrders.Handlers;
using MechanicsSoftware.Domain.Entities;
using MechanicsSoftware.UnitTests.Helpers;
using Moq;

namespace MechanicsSoftware.UnitTests.Application.ServiceOrders;

public class GetDailyServiceOrderVolumeUseCaseTests
{
    [Fact]
    public async Task ExecuteAsync_ReturnsOpenedAndClosedOrdersPerDay()
    {
        var opened = ServiceOrder.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var completed = ServiceOrder.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        completed.StartDiagnosis();
        completed.AddServiceItem(Guid.NewGuid(), "Oil Change", new(5000), 1);
        completed.GenerateBudget();
        completed.SendBudget();
        completed.Approve();
        completed.Complete();

        var mockOrders = MockDbSetHelper.CreateMockDbSet([opened, completed]);
        var db = new Mock<IAppDbContext>();
        db.Setup(context => context.ServiceOrders).Returns(mockOrders.Object);

        var date = opened.CreatedAt.Date;
        var result = await new GetDailyServiceOrderVolumeHandler(db.Object)
            .ExecuteAsync(date, date);

        result.Should().ContainSingle();
        result[0].Opened.Should().Be(2);
        result[0].Closed.Should().Be(completed.CompletedAt?.Date == date ? 1 : 0);
    }
}