using MechanicsSoftware.Application.Abstractions;
using MechanicsSoftware.Domain.Exceptions;
using MechanicsSoftware.Domain.ValueObjects;
using TorqueOS.Contracts.Billing;

namespace MechanicsSoftware.Application.UseCases.ServiceOrders.Handlers;

public sealed class RequestBudgetHandler(IAppDbContext db, IEventPublisher eventPublisher)
{
    public const string ServiceItemType = "Service";
    public const string PartItemType = "Part";

    public async Task<ServiceOrderResponse> ExecuteAsync(
        Guid serviceOrderId, CancellationToken cancellationToken = default)
    {
        var order = await db.ServiceOrders.FindFullAsync(serviceOrderId, cancellationToken);

        if (!order.Status.Is(ServiceOrderStatus.Status.InDiagnosis))
            throw new ConflictException(
                $"Order must be IN_DIAGNOSIS to request a budget. Current status: {order.Status}.");

        if (order.ServiceItems.Count == 0)
            throw new DomainException("A budget requires at least one service item.");

        var items = order.ServiceItems
            .Select(item => new RequestBudgetItem(
                ServiceItemType, item.ServiceId, item.ServiceName, item.UnitPrice.Cents, item.Quantity))
            .Concat(order.PartItems.Select(item => new RequestBudgetItem(
                PartItemType, item.PartId, item.PartName, item.UnitPrice.Cents, item.Quantity)))
            .ToList();

        await eventPublisher.PublishAsync(
            new RequestBudget(order.Id, order.CustomerId, items), order.Id, cancellationToken);

        await db.SaveChangesAsync(cancellationToken);

        return ServiceOrderResponse.From(order);
    }
}
