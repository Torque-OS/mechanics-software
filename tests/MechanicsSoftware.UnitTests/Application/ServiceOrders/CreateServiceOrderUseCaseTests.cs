using FluentAssertions;
using MechanicsSoftware.Application.Exceptions;
using MechanicsSoftware.Application.UseCases.ServiceOrders;
using MechanicsSoftware.Application.UseCases.ServiceOrders.Commands;
using MechanicsSoftware.Application.UseCases.ServiceOrders.Handlers;
using MechanicsSoftware.Application.UseCases.ServiceOrders.Queries;
using MechanicsSoftware.UnitTests.Helpers;
using MechanicsSoftware.Domain.Entities;
using MechanicsSoftware.Domain.ValueObjects;
using MechanicsSoftware.Domain.Enums;
using MechanicsSoftware.Domain.Exceptions;

namespace MechanicsSoftware.UnitTests.Application.ServiceOrders;

public class CreateServiceOrderUseCaseTests
{
    private static readonly Guid CustomerId = Guid.NewGuid();
    private static readonly Guid VehicleId  = Guid.NewGuid();

    private static Customer BuildCustomer() =>
        Customer.Create(CustomerId, "Joel Silva", "529.982.247-25", PersonType.INDIVIDUAL, "joel@email.com", "11999999999");

    private static Vehicle BuildVehicle() =>
        Vehicle.Create(VehicleId, new LicensePlate("ABC1234"), "Toyota", "Corolla", 2020, CustomerId);

    [Fact]
    public async Task ExecuteAsync_ValidRequest_CreatesOrderWithStatusReceived()
    {
        await using var db = InMemoryDbContextHelper.Create();
        db.Customers.Add(BuildCustomer());
        db.Vehicles.Add(BuildVehicle());
        await db.SaveChangesAsync();

        var useCase = new CreateServiceOrderHandler(db, HandlerStubs.Logger<CreateServiceOrderHandler>());
        var result = await useCase.ExecuteAsync(new CreateServiceOrderCommand(CustomerId, VehicleId));

        result.Id.Should().NotBeEmpty();
        result.CustomerId.Should().Be(CustomerId);
        result.VehicleId.Should().Be(VehicleId);
        result.Status.Should().Be("RECEIVED");
    }

    [Fact]
    public async Task ExecuteAsync_CustomerNotFound_ThrowsNotFoundException()
    {
        await using var db = InMemoryDbContextHelper.Create();
        db.Vehicles.Add(BuildVehicle());
        await db.SaveChangesAsync();

        var useCase = new CreateServiceOrderHandler(db, HandlerStubs.Logger<CreateServiceOrderHandler>());
        var act = async () => await useCase.ExecuteAsync(new CreateServiceOrderCommand(CustomerId, VehicleId));

        await act.Should().ThrowAsync<NotFoundException>().WithMessage($"*{CustomerId}*");
    }

    [Fact]
    public async Task ExecuteAsync_VehicleNotFound_ThrowsNotFoundException()
    {
        await using var db = InMemoryDbContextHelper.Create();
        db.Customers.Add(BuildCustomer());
        await db.SaveChangesAsync();

        var useCase = new CreateServiceOrderHandler(db, HandlerStubs.Logger<CreateServiceOrderHandler>());
        var act = async () => await useCase.ExecuteAsync(new CreateServiceOrderCommand(CustomerId, VehicleId));

        await act.Should().ThrowAsync<NotFoundException>().WithMessage($"*{VehicleId}*");
    }
}
