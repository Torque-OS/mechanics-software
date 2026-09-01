using MechanicsSoftware.Application.Abstractions;
using MechanicsSoftware.Application.Exceptions;
using MechanicsSoftware.Application.UseCases.ServiceOrders.Commands;
using MechanicsSoftware.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace MechanicsSoftware.Application.UseCases.ServiceOrders.Handlers;

public sealed class CreateServiceOrderHandler(
    IAppDbContext db,
    ILogger<CreateServiceOrderHandler> logger,
    IServiceOrderMetrics? metrics = null)
{
    public async Task<ServiceOrderResponse> ExecuteAsync(
        CreateServiceOrderCommand command, CancellationToken cancellationToken = default)
    {
        _ = await db.Customers.FindAsync([command.CustomerId], cancellationToken)
            ?? throw new NotFoundException(nameof(Customer), command.CustomerId);

        _ = await db.Vehicles.FindAsync([command.VehicleId], cancellationToken)
            ?? throw new NotFoundException(nameof(Vehicle), command.VehicleId);

        var order = ServiceOrder.Create(Guid.NewGuid(), command.CustomerId, command.VehicleId);

        db.ServiceOrders.Add(order);
        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Service order opened {EventName} {ServiceOrderId} {OccurredAt}",
            "service_order.opened",
            order.Id,
            order.CreatedAt);
        metrics?.OrderOpened();
        metrics?.OrderStatusChanged(order.Status.ToString());

        return ServiceOrderResponse.From(order);
    }
}
