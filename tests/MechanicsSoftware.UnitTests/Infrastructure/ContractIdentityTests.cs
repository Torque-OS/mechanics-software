using FluentAssertions;
using MassTransit;
using TorqueOS.Contracts.Billing;
using TorqueOS.Contracts.Execution;
using TorqueOS.Contracts.ServiceOrders;

namespace MechanicsSoftware.UnitTests.Infrastructure;

/// <summary>
/// Pins the wire identity of every contract copied into this repo from
/// mechanics-billing-service and mechanics-execution-service (ADR-011). A mismatch here means a
/// copy has drifted from the type that actually owns the contract — MassTransit would silently
/// never route the message.
/// </summary>
public class ContractIdentityTests
{
    [Theory]
    [InlineData(typeof(ServiceOrderOpened), "urn:message:TorqueOS.Contracts.ServiceOrders:ServiceOrderOpened")]
    [InlineData(typeof(BudgetGenerated), "urn:message:TorqueOS.Contracts.Billing:BudgetGenerated")]
    [InlineData(typeof(BudgetFailed), "urn:message:TorqueOS.Contracts.Billing:BudgetFailed")]
    [InlineData(typeof(BudgetApproved), "urn:message:TorqueOS.Contracts.Billing:BudgetApproved")]
    [InlineData(typeof(BudgetRejected), "urn:message:TorqueOS.Contracts.Billing:BudgetRejected")]
    [InlineData(typeof(PaymentConfirmed), "urn:message:TorqueOS.Contracts.Billing:PaymentConfirmed")]
    [InlineData(typeof(PaymentFailed), "urn:message:TorqueOS.Contracts.Billing:PaymentFailed")]
    [InlineData(typeof(CancelBudget), "urn:message:TorqueOS.Contracts.Billing:CancelBudget")]
    [InlineData(typeof(EnqueueExecution), "urn:message:TorqueOS.Contracts.Execution:EnqueueExecution")]
    [InlineData(typeof(ExecutionCompleted), "urn:message:TorqueOS.Contracts.Execution:ExecutionCompleted")]
    [InlineData(typeof(ExecutionFailed), "urn:message:TorqueOS.Contracts.Execution:ExecutionFailed")]
    public void MessageIdentity_IsStable(Type contract, string expectedUrn)
    {
        MessageUrn.ForType(contract).ToString().Should().Be(expectedUrn);
    }
}
