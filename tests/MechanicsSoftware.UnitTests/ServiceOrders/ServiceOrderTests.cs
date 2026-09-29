using FluentAssertions;
using MechanicsSoftware.Domain.Entities;
using MechanicsSoftware.Domain.ValueObjects;
using MechanicsSoftware.Domain.Enums;
using MechanicsSoftware.Domain.Exceptions;

namespace MechanicsSoftware.UnitTests.Domain.ServiceOrders;

public class ServiceOrderTests
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
    // Billing Service (ADR-011); it both records the reference and moves to AwaitingApproval.
    private static ServiceOrder AwaitingApproval()
    {
        var o = InDiagnosis();
        o.AddServiceItem(Guid.NewGuid(), "Oil Change", new Money(5000), 1);
        o.AttachBudget(Guid.NewGuid(), new Money(5000));
        return o;
    }

    private static ServiceOrder AwaitingPayment()
    {
        var o = AwaitingApproval();
        o.ConfirmBudgetApproval();
        return o;
    }

    private static ServiceOrder InExecution()
    {
        var o = AwaitingPayment();
        o.ConfirmPayment();
        return o;
    }

    [Fact]
    public void Create_ValidArgs_StatusIsReceived()
    {
        var order = NewOrder();
        order.Status.Value.Should().Be(ServiceOrderStatus.Status.Received);
    }

    [Fact]
    public void Create_EmptyCustomerId_Throws()
    {
        var act = () => ServiceOrder.Create(Guid.NewGuid(), Guid.Empty, Guid.NewGuid());
        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Create_EmptyVehicleId_Throws()
    {
        var act = () => ServiceOrder.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.Empty);
        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void StartDiagnosis_FromReceived_StatusIsInDiagnosis()
    {
        var order = NewOrder();
        order.StartDiagnosis();
        order.Status.Value.Should().Be(ServiceOrderStatus.Status.InDiagnosis);
    }

    [Fact]
    public void StartDiagnosis_AlreadyInDiagnosis_Throws()
    {
        var order = InDiagnosis();
        var act = () => order.StartDiagnosis();
        act.Should().Throw<InvalidStatusTransitionException>();
    }

    [Fact]
    public void AddServiceItem_InDiagnosis_AddsItem()
    {
        var order = InDiagnosis();
        var item = order.AddServiceItem(Guid.NewGuid(), "Alignment", new Money(8000), 1);
        order.ServiceItems.Should().HaveCount(1);
        item.ServiceName.Should().Be("Alignment");
        item.UnitPrice.Cents.Should().Be(8000);
    }

    [Fact]
    public void AddServiceItem_SnapshotsPrice()
    {
        var order = InDiagnosis();
        order.AddServiceItem(Guid.NewGuid(), "Brake Job", new Money(15000), 2);
        order.ServiceItems.First().UnitPrice.Cents.Should().Be(15000);
    }

    [Fact]
    public void AddServiceItem_WhenNotInDiagnosis_Throws()
    {
        var order = NewOrder();
        var act = () => order.AddServiceItem(Guid.NewGuid(), "Oil Change", new Money(5000), 1);
        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void AddServiceItem_ZeroQuantity_Throws()
    {
        var order = InDiagnosis();
        var act = () => order.AddServiceItem(Guid.NewGuid(), "Oil Change", new Money(5000), 0);
        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void AddPartItem_Available_AddsItem()
    {
        var order = InDiagnosis();
        var item = order.AddPartItem(Guid.NewGuid(), "Oil Filter", new Money(2500), 1, PartAvailability.Available);
        order.PartItems.Should().HaveCount(1);
        item.Availability.Should().Be(PartAvailability.Available);
    }

    [Fact]
    public void AddPartItem_Unavailable_MarkedCorrectly()
    {
        var order = InDiagnosis();
        var item = order.AddPartItem(Guid.NewGuid(), "Rare Gasket", new Money(9000), 1, PartAvailability.Unavailable);
        item.Availability.Should().Be(PartAvailability.Unavailable);
    }

    [Fact]
    public void AddPartItem_WhenNotInDiagnosis_Throws()
    {
        var order = NewOrder();
        var act = () => order.AddPartItem(Guid.NewGuid(), "Filter", new Money(2500), 1, PartAvailability.Available);
        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void AttachBudget_FromInDiagnosis_StatusIsAwaitingApproval()
    {
        var order = InDiagnosis();
        order.AddServiceItem(Guid.NewGuid(), "Oil Change", new Money(5000), 1);
        order.AttachBudget(Guid.NewGuid(), new Money(5000));
        order.Status.Value.Should().Be(ServiceOrderStatus.Status.AwaitingApproval);
    }

    [Fact]
    public void AttachBudget_SetsBudgetIdAndTotal()
    {
        var budgetId = Guid.NewGuid();
        var order = InDiagnosis();
        order.AttachBudget(budgetId, new Money(7000));
        order.BudgetId.Should().Be(budgetId);
        order.BudgetTotal!.Cents.Should().Be(7000);
    }

    [Fact]
    public void AttachBudget_EmptyBudgetId_Throws()
    {
        var order = InDiagnosis();
        var act = () => order.AttachBudget(Guid.Empty, new Money(5000));
        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void AttachBudget_NullTotal_Throws()
    {
        var order = InDiagnosis();
        var act = () => order.AttachBudget(Guid.NewGuid(), null!);
        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void AttachBudget_WhenNotInDiagnosis_Throws()
    {
        var order = NewOrder();
        var act = () => order.AttachBudget(Guid.NewGuid(), new Money(5000));
        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void ConfirmBudgetApproval_FromAwaitingApproval_StatusIsAwaitingPayment()
    {
        var order = AwaitingApproval();
        order.ConfirmBudgetApproval();
        order.Status.Value.Should().Be(ServiceOrderStatus.Status.AwaitingPayment);
    }

    [Fact]
    public void ConfirmBudgetApproval_WhenNotAwaitingApproval_Throws()
    {
        var order = InDiagnosis();
        var act = () => order.ConfirmBudgetApproval();
        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void ConfirmPayment_FromAwaitingPayment_StatusIsInExecution()
    {
        var order = AwaitingPayment();
        order.ConfirmPayment();
        order.Status.Value.Should().Be(ServiceOrderStatus.Status.InExecution);
    }

    [Fact]
    public void ConfirmPayment_WhenNotAwaitingPayment_Throws()
    {
        var order = AwaitingApproval();
        var act = () => order.ConfirmPayment();
        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Cancel_FromAwaitingApproval_StatusIsCancelled()
    {
        var order = AwaitingApproval();
        order.Cancel();
        order.Status.Value.Should().Be(ServiceOrderStatus.Status.Cancelled);
    }

    [Fact]
    public void Cancel_FromAwaitingPayment_StatusIsCancelled()
    {
        var order = AwaitingPayment();
        order.Cancel();
        order.Status.Value.Should().Be(ServiceOrderStatus.Status.Cancelled);
    }

    [Fact]
    public void Cancel_FromInExecution_StatusIsCancelled()
    {
        var order = InExecution();
        order.Cancel();
        order.Status.Value.Should().Be(ServiceOrderStatus.Status.Cancelled);
    }

    [Fact]
    public void Cancel_WhenReceived_Throws()
    {
        var order = NewOrder();
        var act = () => order.Cancel();
        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void StartExecution_WhenInExecution_DoesNotThrow()
    {
        var order = InExecution();
        var act = () => order.StartExecution();
        act.Should().NotThrow();
    }

    [Fact]
    public void StartExecution_WhenNotInExecution_Throws()
    {
        var order = AwaitingApproval();
        var act = () => order.StartExecution();
        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Complete_FromInExecution_StatusIsCompleted()
    {
        var order = InExecution();
        order.Complete();
        order.Status.Value.Should().Be(ServiceOrderStatus.Status.Completed);
    }

    [Fact]
    public void Complete_WhenNotInExecution_Throws()
    {
        var order = AwaitingApproval();
        var act = () => order.Complete();
        act.Should().Throw<InvalidStatusTransitionException>();
    }

    [Fact]
    public void Deliver_FromCompleted_StatusIsDelivered()
    {
        var order = InExecution();
        order.Complete();
        order.Deliver();
        order.Status.Value.Should().Be(ServiceOrderStatus.Status.Delivered);
    }

    [Fact]
    public void Deliver_RecordsDeliveryTimestamp()
    {
        var order = InExecution();
        order.Complete();
        var before = DateTime.UtcNow;
        order.Deliver();
        order.DeliveredAt.Should().NotBeNull();
        order.DeliveredAt.Should().BeOnOrAfter(before);
    }

    [Fact]
    public void Deliver_WhenNotCompleted_Throws()
    {
        var order = InExecution();
        var act = () => order.Deliver();
        act.Should().Throw<InvalidStatusTransitionException>();
    }

    [Fact]
    public void FullLifecycle_HappyPath_AllTransitionsSucceed()
    {
        var order = NewOrder();

        order.StartDiagnosis();
        order.AddServiceItem(Guid.NewGuid(), "Full Service", new Money(20000), 1);
        order.AddPartItem(Guid.NewGuid(), "Air Filter", new Money(3000), 2, PartAvailability.Available);
        order.AddPartItem(Guid.NewGuid(), "Rare Sensor", new Money(50000), 1, PartAvailability.Unavailable);

        order.AttachBudget(Guid.NewGuid(), new Money(26000));
        order.BudgetTotal!.Cents.Should().Be(26000);

        order.ConfirmBudgetApproval();
        order.ConfirmPayment();
        order.StartExecution();
        order.Complete();
        order.Deliver();

        order.Status.Value.Should().Be(ServiceOrderStatus.Status.Delivered);
        order.DeliveredAt.Should().NotBeNull();
    }

    [Fact]
    public void FullLifecycle_Rejection_StatusIsCancelled()
    {
        var order = NewOrder();
        order.StartDiagnosis();
        order.AddServiceItem(Guid.NewGuid(), "Inspection", new Money(10000), 1);
        order.AttachBudget(Guid.NewGuid(), new Money(10000));
        order.Cancel();

        order.Status.Value.Should().Be(ServiceOrderStatus.Status.Cancelled);
    }

    [Fact]
    public void AddServiceItem_EmptyServiceId_Throws()
    {
        var order = InDiagnosis();
        var act = () => order.AddServiceItem(Guid.Empty, "Oil Change", new Money(5000), 1);
        act.Should().Throw<DomainException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void AddServiceItem_EmptyName_Throws(string name)
    {
        var order = InDiagnosis();
        var act = () => order.AddServiceItem(Guid.NewGuid(), name, new Money(5000), 1);
        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void AddServiceItem_NullPrice_Throws()
    {
        var order = InDiagnosis();
        var act = () => order.AddServiceItem(Guid.NewGuid(), "Oil Change", null!, 1);
        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void AddPartItem_EmptyPartId_Throws()
    {
        var order = InDiagnosis();
        var act = () => order.AddPartItem(Guid.Empty, "Oil Filter", new Money(2500), 1, PartAvailability.Available);
        act.Should().Throw<DomainException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void AddPartItem_EmptyName_Throws(string name)
    {
        var order = InDiagnosis();
        var act = () => order.AddPartItem(Guid.NewGuid(), name, new Money(2500), 1, PartAvailability.Available);
        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void AddPartItem_NullPrice_Throws()
    {
        var order = InDiagnosis();
        var act = () => order.AddPartItem(Guid.NewGuid(), "Oil Filter", null!, 1, PartAvailability.Available);
        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void AddPartItem_ZeroQuantity_Throws()
    {
        var order = InDiagnosis();
        var act = () => order.AddPartItem(Guid.NewGuid(), "Oil Filter", new Money(2500), 0, PartAvailability.Available);
        act.Should().Throw<DomainException>();
    }
}
