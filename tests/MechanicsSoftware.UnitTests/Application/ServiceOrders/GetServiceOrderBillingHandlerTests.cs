using FluentAssertions;
using MechanicsSoftware.Application.Abstractions;
using MechanicsSoftware.Application.Exceptions;
using MechanicsSoftware.Application.UseCases.ServiceOrders.Handlers;
using MechanicsSoftware.Domain.Entities;
using MechanicsSoftware.Infrastructure.Persistence;
using MechanicsSoftware.UnitTests.Helpers;
using Moq;

namespace MechanicsSoftware.UnitTests.Application.ServiceOrders;

public class GetServiceOrderBillingHandlerTests
{
    private readonly Mock<IBillingClient> _billing = new();

    private static readonly BillingBudget Budget = new(
        Guid.NewGuid(), "APPROVED", 15_000, DateTime.UtcNow, DateTime.UtcNow,
        [new BillingBudgetItem("Service", Guid.NewGuid(), "Oil change", 15_000, 1, 15_000)]);

    private static readonly BillingPayment Payment = new(
        Guid.NewGuid(), "PENDING", 15_000, "qr-data", null, DateTime.UtcNow, null, null);

    private static async Task<Guid> SeedOrderAsync(AppDbContext db)
    {
        var order = ServiceOrder.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        db.ServiceOrders.Add(order);
        await db.SaveChangesAsync();
        return order.Id;
    }

    private GetServiceOrderBillingHandler Handler(AppDbContext db) =>
        new(db, _billing.Object, HandlerStubs.Logger<GetServiceOrderBillingHandler>());

    [Fact]
    public async Task ExecuteAsync_BudgetAndPayment_ReturnsBoth()
    {
        await using var db = InMemoryDbContextHelper.Create();
        var orderId = await SeedOrderAsync(db);
        _billing.Setup(b => b.GetBudgetByServiceOrderAsync(orderId, It.IsAny<CancellationToken>())).ReturnsAsync(Budget);
        _billing.Setup(b => b.GetPaymentByBudgetAsync(Budget.Id, It.IsAny<CancellationToken>())).ReturnsAsync(Payment);

        var result = await Handler(db).ExecuteAsync(orderId);

        result.ServiceOrderId.Should().Be(orderId);
        result.BillingAvailable.Should().BeTrue();
        result.UnavailableReason.Should().BeNull();
        result.Budget.Should().Be(Budget);
        result.Payment.Should().Be(Payment);
    }

    [Fact]
    public async Task ExecuteAsync_NoBudgetYet_ReturnsAvailableWithoutBudgetAndDoesNotAskForThePayment()
    {
        await using var db = InMemoryDbContextHelper.Create();
        var orderId = await SeedOrderAsync(db);

        var result = await Handler(db).ExecuteAsync(orderId);

        result.BillingAvailable.Should().BeTrue();
        result.Budget.Should().BeNull();
        result.Payment.Should().BeNull();
        _billing.Verify(b => b.GetPaymentByBudgetAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_BudgetWithoutPayment_ReturnsTheBudgetOnly()
    {
        await using var db = InMemoryDbContextHelper.Create();
        var orderId = await SeedOrderAsync(db);
        _billing.Setup(b => b.GetBudgetByServiceOrderAsync(orderId, It.IsAny<CancellationToken>())).ReturnsAsync(Budget);

        var result = await Handler(db).ExecuteAsync(orderId);

        result.Budget.Should().Be(Budget);
        result.Payment.Should().BeNull();
    }

    [Fact]
    public async Task ExecuteAsync_BillingUnavailable_DegradesInsteadOfFailing()
    {
        await using var db = InMemoryDbContextHelper.Create();
        var orderId = await SeedOrderAsync(db);
        _billing.Setup(b => b.GetBudgetByServiceOrderAsync(orderId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new BillingUnavailableException("Billing Service did not answer in time."));

        var result = await Handler(db).ExecuteAsync(orderId);

        result.BillingAvailable.Should().BeFalse();
        result.UnavailableReason.Should().Be("Billing Service did not answer in time.");
        result.Budget.Should().BeNull();
        result.Payment.Should().BeNull();
    }

    [Fact]
    public async Task ExecuteAsync_UnknownOrder_ThrowsNotFoundWithoutCallingBilling()
    {
        await using var db = InMemoryDbContextHelper.Create();

        var act = () => Handler(db).ExecuteAsync(Guid.NewGuid());

        await act.Should().ThrowAsync<NotFoundException>();
        _billing.VerifyNoOtherCalls();
    }
}
