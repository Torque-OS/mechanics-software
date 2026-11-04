using MechanicsSoftware.Application.Abstractions;
using MechanicsSoftware.Application.Exceptions;
using MechanicsSoftware.Domain.Entities;
using MechanicsSoftware.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace MechanicsSoftware.Application.UseCases.ServiceOrders.Handlers;

/// <summary>
/// Status timeline of a service order, oldest first. Every status change — from the staff endpoints or
/// from the saga events consumed from Billing and Execution (F4-12) — is recorded by
/// <c>AppDbContext</c> in <c>service_order_status_history</c>.
/// </summary>
public sealed class GetServiceOrderHistoryHandler(IAppDbContext db)
{
    public async Task<ServiceOrderHistoryResponse> ExecuteAsync(
        Guid serviceOrderId, CancellationToken cancellationToken = default)
    {
        var order = await db.ServiceOrders
            .FirstOrDefaultAsync(o => o.Id == serviceOrderId, cancellationToken)
            ?? throw new NotFoundException(nameof(ServiceOrder), serviceOrderId);

        var history = await db.ServiceOrderStatusHistory
            .Where(entry => entry.ServiceOrderId == serviceOrderId)
            .OrderBy(entry => entry.EnteredAt)
            .ToListAsync(cancellationToken);

        return new ServiceOrderHistoryResponse(
            order.Id,
            order.Status.ToString(),
            history
                .Select(entry => new ServiceOrderStatusHistoryEntry(
                    new ServiceOrderStatus(entry.Status).ToString(),
                    entry.EnteredAt))
                .ToList());
    }
}
