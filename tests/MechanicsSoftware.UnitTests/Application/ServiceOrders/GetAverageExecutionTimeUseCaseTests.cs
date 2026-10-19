using FluentAssertions;
using MechanicsSoftware.Application.Abstractions;
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

public class GetAverageExecutionTimeUseCaseTests
{
    [Fact]
    public async Task ExecuteAsync_NoCompletedOrders_ReturnsZero()
    {
        List<ServiceOrder> orders =
        [
            ServiceOrder.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()),
        ];

        var mockOrders = MockDbSetHelper.CreateMockDbSet(orders);
        var db = new Mock<IAppDbContext>();
        db.Setup(d => d.ServiceOrders).Returns(mockOrders.Object);

        var result = await new GetAverageExecutionTimeHandler(db.Object).ExecuteAsync();

        result.AverageHours.Should().Be(0);
        result.OrderCount.Should().Be(0);
    }

    [Fact]
    public async Task ExecuteAsync_WithCompletedOrders_ReturnsAverage()
    {
        var order = ServiceOrder.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        order.StartDiagnosis();
        order.AddServiceItem(Guid.NewGuid(), "Oil Change", new Money(5000), 1);
        order.AttachBudget(Guid.NewGuid(), new Money(5000));
        order.ConfirmBudgetApproval();
        order.ConfirmPayment();
        order.Complete();

        var mockOrders = MockDbSetHelper.CreateMockDbSet([order]);
        var db = new Mock<IAppDbContext>();
        db.Setup(d => d.ServiceOrders).Returns(mockOrders.Object);

        var result = await new GetAverageExecutionTimeHandler(db.Object).ExecuteAsync();

        result.OrderCount.Should().Be(1);
        result.AverageHours.Should().BeGreaterThanOrEqualTo(0);
    }
}
