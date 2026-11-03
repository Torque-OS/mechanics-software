using FluentAssertions;
using MassTransit;
using TorqueOS.Contracts.ServiceOrders;

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
    public void MessageIdentity_IsStable(Type contract, string expectedUrn)
    {
        MessageUrn.ForType(contract).ToString().Should().Be(expectedUrn);
    }
}
