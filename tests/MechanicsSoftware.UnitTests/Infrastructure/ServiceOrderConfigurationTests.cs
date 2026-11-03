using FluentAssertions;
using MechanicsSoftware.Infrastructure.Persistence.Configurations;
using MechanicsSoftware.UnitTests.Helpers;
using MechanicsSoftware.Domain.Entities;
using MechanicsSoftware.Domain.ValueObjects;
using MechanicsSoftware.Domain.Enums;
using MechanicsSoftware.Domain.Exceptions;

namespace MechanicsSoftware.UnitTests.Infrastructure;

public class ServiceOrderConfigurationTests
{
    private static ServiceOrder NewOrder() =>
        ServiceOrder.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

    private static ServiceOrder InDiagnosis()
    {
        var o = NewOrder();
        o.StartDiagnosis();
        return o;
    }

    // AttachBudget represents the OS Service receiving the BudgetGenerated event from the
    // Billing Service (ADR-011).
    private static ServiceOrder WithBudget()
    {
        var o = InDiagnosis();
        o.AddServiceItem(Guid.NewGuid(), "Oil Change", new Money(5000), 1);
        o.AttachBudget(Guid.NewGuid(), new Money(5000));
        return o;
    }

    private static async Task<ServiceOrder> RoundTrip(ServiceOrder order)
    {
        await using var ctx = InMemoryDbContextHelper.Create();
        ctx.ServiceOrders.Add(order);
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();
        return (await ctx.ServiceOrders.FindAsync(order.Id))!;
    }

    [Fact]
    public async Task ServiceOrder_StatusReceived_RoundTrips()
    {
        var loaded = await RoundTrip(NewOrder());
        loaded.Status.Value.Should().Be(ServiceOrderStatus.Status.Received);
    }

    [Fact]
    public async Task ServiceOrder_StatusInDiagnosis_RoundTrips()
    {
        var loaded = await RoundTrip(InDiagnosis());
        loaded.Status.Value.Should().Be(ServiceOrderStatus.Status.InDiagnosis);
    }

    [Fact]
    public async Task ServiceOrder_StatusAwaitingApproval_RoundTrips()
    {
        var o = WithBudget();
        var loaded = await RoundTrip(o);
        loaded.Status.Value.Should().Be(ServiceOrderStatus.Status.AwaitingApproval);
    }

    [Fact]
    public async Task ServiceOrder_StatusAwaitingPayment_RoundTrips()
    {
        var o = WithBudget();
        o.ConfirmBudgetApproval();
        var loaded = await RoundTrip(o);
        loaded.Status.Value.Should().Be(ServiceOrderStatus.Status.AwaitingPayment);
    }

    [Fact]
    public async Task ServiceOrder_StatusInExecution_RoundTrips()
    {
        var o = WithBudget();
        o.ConfirmBudgetApproval();
        o.ConfirmPayment();
        var loaded = await RoundTrip(o);
        loaded.Status.Value.Should().Be(ServiceOrderStatus.Status.InExecution);
    }

    [Fact]
    public async Task ServiceOrder_StatusCompleted_RoundTrips()
    {
        var o = WithBudget();
        o.ConfirmBudgetApproval();
        o.ConfirmPayment();
        o.Complete();
        var loaded = await RoundTrip(o);
        loaded.Status.Value.Should().Be(ServiceOrderStatus.Status.Completed);
    }

    [Fact]
    public async Task ServiceOrder_StatusDelivered_RoundTrips()
    {
        var o = WithBudget();
        o.ConfirmBudgetApproval();
        o.ConfirmPayment();
        o.Complete();
        o.Deliver();
        var loaded = await RoundTrip(o);
        loaded.Status.Value.Should().Be(ServiceOrderStatus.Status.Delivered);
        loaded.DeliveredAt.Should().NotBeNull();
    }

    [Fact]
    public async Task ServiceOrder_StatusCancelled_RoundTrips()
    {
        var o = WithBudget();
        o.Cancel();
        var loaded = await RoundTrip(o);
        loaded.Status.Value.Should().Be(ServiceOrderStatus.Status.Cancelled);
    }

    [Fact]
    public async Task ServiceOrder_BudgetReference_RoundTrips()
    {
        var o = WithBudget();
        var loaded = await RoundTrip(o);
        loaded.BudgetId.Should().Be(o.BudgetId);
        loaded.BudgetTotal!.Cents.Should().Be(5000);
    }

    [Fact]
    public async Task ServiceOrder_NoBudget_BudgetReferenceIsNull()
    {
        var loaded = await RoundTrip(InDiagnosis());
        loaded.BudgetId.Should().BeNull();
        loaded.BudgetTotal.Should().BeNull();
    }

    [Fact]
    public void ParseOrderStatus_UnknownValue_ThrowsInvalidOperationException()
    {
        var act = () => ServiceOrderConfiguration.ParseOrderStatus("UNKNOWN_STATUS");
        act.Should().Throw<InvalidOperationException>().WithMessage("*UNKNOWN_STATUS*");
    }
}
