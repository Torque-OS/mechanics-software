using MechanicsSoftware.Application.Abstractions;
using MechanicsSoftware.Application.Exceptions;
using MechanicsSoftware.Application.UseCases.ServiceOrders.Commands;
using MechanicsSoftware.Contracts.Events;
using MechanicsSoftware.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace MechanicsSoftware.Application.UseCases.ServiceOrders.Handlers;

public sealed class CreateServiceOrderHandler(
    IAppDbContext db,
    IEventPublisher eventPublisher,
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

        // Starts the saga (ADR-011). Published before SaveChangesAsync on purpose: the outbox
        // stores the message in the same transaction as the order, so both are saved or neither is.
        await eventPublisher.PublishAsync(
            new ServiceOrderOpened(order.Id, order.CustomerId, order.VehicleId, order.CreatedAt),
            cancellationToken);

        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Service order opened {EventName} {ServiceOrderId} {OccurredAt}",
            "service_order.opened",
            order.Id,
            order.CreatedAt);
        metrics?.OrderOpened();

        return ServiceOrderResponse.From(order);
    }
}
