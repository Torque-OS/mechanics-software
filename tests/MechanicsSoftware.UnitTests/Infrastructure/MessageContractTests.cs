using FluentAssertions;
using MassTransit;
using TorqueOS.Contracts.ServiceOrders;
using TorqueOS.Contracts.Billing;
using TorqueOS.Contracts.Execution;

namespace MechanicsSoftware.UnitTests.Infrastructure;

/// <summary>
/// MassTransit routes messages by namespace + type name, and the consumers in other repositories
/// declare the same identity. Renaming a contract by accident would silently stop delivery, so the
/// identity is pinned here.
/// </summary>
public class MessageContractTests
{
    [Theory]
    [InlineData(typeof(ServiceOrderOpened), "urn:message:TorqueOS.Contracts.ServiceOrders:ServiceOrderOpened")]
    [InlineData(typeof(BudgetGenerated), "urn:message:TorqueOS.Contracts.Billing:BudgetGenerated")]
    [InlineData(typeof(BudgetFailed), "urn:message:TorqueOS.Contracts.Billing:BudgetFailed")]
    [InlineData(typeof(BudgetApproved), "urn:message:TorqueOS.Contracts.Billing:BudgetApproved")]
    [InlineData(typeof(BudgetRejected), "urn:message:TorqueOS.Contracts.Billing:BudgetRejected")]
    [InlineData(typeof(PaymentConfirmed), "urn:message:TorqueOS.Contracts.Billing:PaymentConfirmed")]
    [InlineData(typeof(PaymentFailed), "urn:message:TorqueOS.Contracts.Billing:PaymentFailed")]
    [InlineData(typeof(ExecutionCompleted), "urn:message:TorqueOS.Contracts.Execution:ExecutionCompleted")]
    [InlineData(typeof(ExecutionFailed), "urn:message:TorqueOS.Contracts.Execution:ExecutionFailed")]
    [InlineData(typeof(EnqueueExecution), "urn:message:TorqueOS.Contracts.Execution:EnqueueExecution")]
    [InlineData(typeof(CancelBudget), "urn:message:TorqueOS.Contracts.Billing:CancelBudget")]
    public void MessageIdentity_IsStable(Type contract, string expectedUrn)
    {
        MessageUrn.ForType(contract).ToString().Should().Be(expectedUrn);
    }
}
