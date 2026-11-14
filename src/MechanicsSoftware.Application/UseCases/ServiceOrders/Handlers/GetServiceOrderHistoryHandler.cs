using MechanicsSoftware.Application.Abstractions;
using MechanicsSoftware.Application.Exceptions;
using MechanicsSoftware.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace MechanicsSoftware.Application.UseCases.ServiceOrders.Handlers;

/// <summary>
/// Status timeline of a service order, oldest first. Every status change — from the staff endpoints or
/// from the saga events consumed from Billing and Execution (F4-12) — is recorded by
/// the OS Service history store.
/// </summary>
public sealed class GetServiceOrderHistoryHandler(
    IAppDbContext db,
    IServiceOrderStatusHistoryStore historyStore)
{
    public async Task<ServiceOrderHistoryResponse> ExecuteAsync(
        Guid serviceOrderId, CancellationToken cancellationToken = default)
    {
        var order = await db.ServiceOrders
            .FirstOrDefaultAsync(o => o.Id == serviceOrderId, cancellationToken)
            ?? throw new NotFoundException(nameof(ServiceOrder), serviceOrderId);

        var history = await historyStore.GetAsync(serviceOrderId, cancellationToken);

        return new ServiceOrderHistoryResponse(
            order.Id,
            order.Status.ToString(),
            history
                .Select(entry => new ServiceOrderStatusHistoryEntry(
                    entry.Status,
                    entry.EnteredAt))
                .ToList());
    }
}
