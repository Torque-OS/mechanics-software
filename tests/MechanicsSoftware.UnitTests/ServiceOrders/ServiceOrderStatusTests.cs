using FluentAssertions;
using MechanicsSoftware.Domain.Entities;
using MechanicsSoftware.Domain.ValueObjects;
using MechanicsSoftware.Domain.Enums;
using MechanicsSoftware.Domain.Exceptions;

namespace MechanicsSoftware.UnitTests.Domain.ServiceOrders;

public class ServiceOrderStatusTests
{
    [Fact]
    public void CreateReceived_ReturnsReceivedStatus()
    {
        var status = ServiceOrderStatus.CreateReceived();
        status.Value.Should().Be(ServiceOrderStatus.Status.Received);
    }

    [Fact]
    public void TransitionTo_ReceivedToInDiagnosis_Valid()
    {
        var status = ServiceOrderStatus.CreateReceived();
        var result = status.TransitionTo(ServiceOrderStatus.Status.InDiagnosis);
        result.Value.Should().Be(ServiceOrderStatus.Status.InDiagnosis);
    }

    [Fact]
    public void TransitionTo_InDiagnosisToAwaitingApproval_Valid()
    {
        var status = new ServiceOrderStatus(ServiceOrderStatus.Status.InDiagnosis);
        var result = status.TransitionTo(ServiceOrderStatus.Status.AwaitingApproval);
        result.Value.Should().Be(ServiceOrderStatus.Status.AwaitingApproval);
    }

    [Fact]
    public void TransitionTo_AwaitingApprovalToAwaitingPayment_Valid()
    {
        var status = new ServiceOrderStatus(ServiceOrderStatus.Status.AwaitingApproval);
        var result = status.TransitionTo(ServiceOrderStatus.Status.AwaitingPayment);
        result.Value.Should().Be(ServiceOrderStatus.Status.AwaitingPayment);
    }

    [Fact]
    public void TransitionTo_AwaitingApprovalToCancelled_Valid()
    {
        var status = new ServiceOrderStatus(ServiceOrderStatus.Status.AwaitingApproval);
        var result = status.TransitionTo(ServiceOrderStatus.Status.Cancelled);
        result.Value.Should().Be(ServiceOrderStatus.Status.Cancelled);
    }

    [Fact]
    public void TransitionTo_AwaitingPaymentToInExecution_Valid()
    {
        var status = new ServiceOrderStatus(ServiceOrderStatus.Status.AwaitingPayment);
        var result = status.TransitionTo(ServiceOrderStatus.Status.InExecution);
        result.Value.Should().Be(ServiceOrderStatus.Status.InExecution);
    }

    [Fact]
    public void TransitionTo_AwaitingPaymentToCancelled_Valid()
    {
        var status = new ServiceOrderStatus(ServiceOrderStatus.Status.AwaitingPayment);
        var result = status.TransitionTo(ServiceOrderStatus.Status.Cancelled);
        result.Value.Should().Be(ServiceOrderStatus.Status.Cancelled);
    }

    [Fact]
    public void TransitionTo_InExecutionToCancelled_Valid()
    {
        var status = new ServiceOrderStatus(ServiceOrderStatus.Status.InExecution);
        var result = status.TransitionTo(ServiceOrderStatus.Status.Cancelled);
        result.Value.Should().Be(ServiceOrderStatus.Status.Cancelled);
    }

    [Fact]
    public void TransitionTo_InExecutionToCompleted_Valid()
    {
        var status = new ServiceOrderStatus(ServiceOrderStatus.Status.InExecution);
        var result = status.TransitionTo(ServiceOrderStatus.Status.Completed);
        result.Value.Should().Be(ServiceOrderStatus.Status.Completed);
    }

    [Fact]
    public void TransitionTo_CompletedToDelivered_Valid()
    {
        var status = new ServiceOrderStatus(ServiceOrderStatus.Status.Completed);
        var result = status.TransitionTo(ServiceOrderStatus.Status.Delivered);
        result.Value.Should().Be(ServiceOrderStatus.Status.Delivered);
    }

    [Fact]
    public void TransitionTo_ReceivedToCancelled_ThrowsInvalidStatusTransitionException()
    {
        var status = ServiceOrderStatus.CreateReceived();
        var act = () => status.TransitionTo(ServiceOrderStatus.Status.Cancelled);
        act.Should().Throw<InvalidStatusTransitionException>();
    }

    [Fact]
    public void TransitionTo_InDiagnosisToInExecution_ThrowsInvalidStatusTransitionException()
    {
        var status = new ServiceOrderStatus(ServiceOrderStatus.Status.InDiagnosis);
        var act = () => status.TransitionTo(ServiceOrderStatus.Status.InExecution);
        act.Should().Throw<InvalidStatusTransitionException>();
    }

    [Fact]
    public void TransitionTo_InExecutionToDelivered_ThrowsInvalidStatusTransitionException()
    {
        var status = new ServiceOrderStatus(ServiceOrderStatus.Status.InExecution);
        var act = () => status.TransitionTo(ServiceOrderStatus.Status.Delivered);
        act.Should().Throw<InvalidStatusTransitionException>();
    }

    [Fact]
    public void TransitionTo_DeliveredToAnyStatus_ThrowsInvalidStatusTransitionException()
    {
        var status = new ServiceOrderStatus(ServiceOrderStatus.Status.Delivered);
        var act = () => status.TransitionTo(ServiceOrderStatus.Status.InDiagnosis);
        act.Should().Throw<InvalidStatusTransitionException>();
    }

    [Fact]
    public void TransitionTo_CancelledToAnyStatus_ThrowsInvalidStatusTransitionException()
    {
        var status = new ServiceOrderStatus(ServiceOrderStatus.Status.Cancelled);
        var act = () => status.TransitionTo(ServiceOrderStatus.Status.Received);
        act.Should().Throw<InvalidStatusTransitionException>();
    }

    [Fact]
    public void TransitionTo_CompletedToInExecution_ThrowsInvalidStatusTransitionException()
    {
        var status = new ServiceOrderStatus(ServiceOrderStatus.Status.Completed);
        var act = () => status.TransitionTo(ServiceOrderStatus.Status.InExecution);
        act.Should().Throw<InvalidStatusTransitionException>();
    }

    [Fact]
    public void TransitionTo_InvalidTransition_ExceptionMessageContainsStatuses()
    {
        var status = new ServiceOrderStatus(ServiceOrderStatus.Status.Completed);
        var act = () => status.TransitionTo(ServiceOrderStatus.Status.Received);
        act.Should().Throw<InvalidStatusTransitionException>()
            .WithMessage("*COMPLETED*RECEIVED*");
    }

    [Fact]
    public void ToString_AllStatuses_ReturnUpperSnakeCase()
    {
        new ServiceOrderStatus(ServiceOrderStatus.Status.Received).ToString().Should().Be("RECEIVED");
        new ServiceOrderStatus(ServiceOrderStatus.Status.InDiagnosis).ToString().Should().Be("IN_DIAGNOSIS");
        new ServiceOrderStatus(ServiceOrderStatus.Status.AwaitingApproval).ToString().Should().Be("AWAITING_APPROVAL");
        new ServiceOrderStatus(ServiceOrderStatus.Status.AwaitingPayment).ToString().Should().Be("AWAITING_PAYMENT");
        new ServiceOrderStatus(ServiceOrderStatus.Status.InExecution).ToString().Should().Be("IN_EXECUTION");
        new ServiceOrderStatus(ServiceOrderStatus.Status.Completed).ToString().Should().Be("COMPLETED");
        new ServiceOrderStatus(ServiceOrderStatus.Status.Delivered).ToString().Should().Be("DELIVERED");
        new ServiceOrderStatus(ServiceOrderStatus.Status.Cancelled).ToString().Should().Be("CANCELLED");
    }

    [Fact]
    public void Equality_SameStatus_AreEqual()
    {
        var a = ServiceOrderStatus.CreateReceived();
        var b = ServiceOrderStatus.CreateReceived();
        a.Should().Be(b);
        (a == b).Should().BeTrue();
    }

    [Fact]
    public void Equality_DifferentStatuses_AreNotEqual()
    {
        var received = ServiceOrderStatus.CreateReceived();
        var inDiagnosis = new ServiceOrderStatus(ServiceOrderStatus.Status.InDiagnosis);
        (received != inDiagnosis).Should().BeTrue();
    }

    [Fact]
    public void GetHashCode_SameStatus_SameHash()
    {
        var a = ServiceOrderStatus.CreateReceived();
        var b = ServiceOrderStatus.CreateReceived();
        a.GetHashCode().Should().Be(b.GetHashCode());
    }
}
