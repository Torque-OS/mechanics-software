using MechanicsSoftware.Application.Abstractions;
using MechanicsSoftware.Application.Exceptions;
using MechanicsSoftware.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace MechanicsSoftware.Application.UseCases.ServiceOrders.Handlers;

public sealed class GetServiceOrderStatusHandler(IAppDbContext db)
{
    public async Task<ServiceOrderStatusResponse> ExecuteAsync(
        Guid serviceOrderId, Guid? ownerCustomerId = null, CancellationToken cancellationToken = default)
    {
        var order = await db.ServiceOrders
            .FirstOrDefaultAsync(
                o => o.Id == serviceOrderId
                    && (ownerCustomerId == null || o.CustomerId == ownerCustomerId),
                cancellationToken)
            ?? throw new NotFoundException(nameof(ServiceOrder), serviceOrderId);

        return new ServiceOrderStatusResponse(
            order.Id,
            order.Status.ToString(),
            order.CreatedAt,
            order.DeliveredAt);
    }
}
