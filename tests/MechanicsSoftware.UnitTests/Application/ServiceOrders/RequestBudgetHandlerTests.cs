using FluentAssertions;
using MechanicsSoftware.Application.Abstractions;
using MechanicsSoftware.Application.Exceptions;
using MechanicsSoftware.Application.UseCases.ServiceOrders.Handlers;
using MechanicsSoftware.Domain.Entities;
using MechanicsSoftware.Domain.Enums;
using MechanicsSoftware.Domain.Exceptions;
using MechanicsSoftware.Domain.ValueObjects;
using MechanicsSoftware.UnitTests.Helpers;
using Moq;
using TorqueOS.Contracts.Billing;

namespace MechanicsSoftware.UnitTests.Application.ServiceOrders;

public class RequestBudgetHandlerTests
{
    private readonly Mock<IEventPublisher> _publisher = new();

    private static ServiceOrder OrderInDiagnosis()
    {
        var order = ServiceOrder.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        order.StartDiagnosis();
        return order;
    }

    [Fact]
    public async Task ExecuteAsync_OrderInDiagnosis_PublishesTheItemSnapshotCorrelatedToTheOrder()
    {
        await using var db = InMemoryDbContextHelper.Create();
        var order = OrderInDiagnosis();
        var service = order.AddServiceItem(Guid.NewGuid(), "Oil change", new Money(15_000), 1);
        var part = order.AddPartItem(Guid.NewGuid(), "Oil filter", new Money(4_500), 2, PartAvailability.Available);
        db.ServiceOrders.Add(order);
        await db.SaveChangesAsync();

        var result = await new RequestBudgetHandler(db, _publisher.Object).ExecuteAsync(order.Id);

        result.Status.Should().Be("IN_DIAGNOSIS");
        _publisher.Verify(p => p.PublishAsync(
                It.Is<RequestBudget>(request =>
                    request.ServiceOrderId == order.Id &&
                    request.CustomerId == order.CustomerId &&
                    request.Items.Count == 2 &&
                    request.Items.Any(i => i.Type == "Service" && i.ReferenceId == service.ServiceId &&
                                           i.Description == "Oil change" && i.UnitPriceInCents == 15_000 && i.Quantity == 1) &&
                    request.Items.Any(i => i.Type == "Part" && i.ReferenceId == part.PartId &&
                                           i.Description == "Oil filter" && i.UnitPriceInCents == 4_500 && i.Quantity == 2)),
                order.Id,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_WithoutServiceItem_ThrowsDomainExceptionAndPublishesNothing()
    {
        await using var db = InMemoryDbContextHelper.Create();
        var order = OrderInDiagnosis();
        order.AddPartItem(Guid.NewGuid(), "Oil filter", new Money(4_500), 2, PartAvailability.Available);
        db.ServiceOrders.Add(order);
        await db.SaveChangesAsync();

        var act = () => new RequestBudgetHandler(db, _publisher.Object).ExecuteAsync(order.Id);

        await act.Should().ThrowAsync<DomainException>().WithMessage("*service item*");
        _publisher.Verify(p => p.PublishAsync(It.IsAny<RequestBudget>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_OrderNotInDiagnosis_ThrowsConflictAndPublishesNothing()
    {
        await using var db = InMemoryDbContextHelper.Create();
        var order = ServiceOrder.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        db.ServiceOrders.Add(order);
        await db.SaveChangesAsync();

        var act = () => new RequestBudgetHandler(db, _publisher.Object).ExecuteAsync(order.Id);

        await act.Should().ThrowAsync<ConflictException>().WithMessage("*IN_DIAGNOSIS*");
        _publisher.Verify(p => p.PublishAsync(It.IsAny<RequestBudget>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_UnknownOrder_ThrowsNotFound()
    {
        await using var db = InMemoryDbContextHelper.Create();

        var act = () => new RequestBudgetHandler(db, _publisher.Object).ExecuteAsync(Guid.NewGuid());

        await act.Should().ThrowAsync<NotFoundException>();
    }
}
