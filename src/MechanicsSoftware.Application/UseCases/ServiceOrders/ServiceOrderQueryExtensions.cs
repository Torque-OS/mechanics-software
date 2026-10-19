using MechanicsSoftware.Application.Exceptions;
using MechanicsSoftware.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace MechanicsSoftware.Application.UseCases.ServiceOrders;

internal static class ServiceOrderQueryExtensions
{
    internal static async Task<ServiceOrder> FindFullAsync(
        this IQueryable<ServiceOrder> query,
        Guid id,
        CancellationToken cancellationToken = default) =>
        await query
            .Include(o => o.ServiceItems)
            .Include(o => o.PartItems)
            .FirstOrDefaultAsync(o => o.Id == id, cancellationToken)
        ?? throw new NotFoundException(nameof(ServiceOrder), id);
}
