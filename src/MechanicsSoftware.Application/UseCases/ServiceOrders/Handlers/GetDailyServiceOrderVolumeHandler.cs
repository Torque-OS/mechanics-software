using MechanicsSoftware.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace MechanicsSoftware.Application.UseCases.ServiceOrders.Handlers;

public sealed class GetDailyServiceOrderVolumeHandler(IAppDbContext db)
{
    public async Task<IReadOnlyList<DailyServiceOrderVolumeResponse>> ExecuteAsync(
        DateTime? from = null,
        DateTime? to = null,
        CancellationToken cancellationToken = default)
    {
        var startDate = (from ?? DateTime.UtcNow.Date.AddDays(-29)).Date;
        var endDate = (to ?? DateTime.UtcNow.Date).Date;

        if (endDate < startDate)
            throw new ArgumentException("The 'to' date must be on or after the 'from' date.");

        var orders = await db.ServiceOrders
            .Where(order => order.CreatedAt < endDate.AddDays(1)
                || (order.CompletedAt != null && order.CompletedAt < endDate.AddDays(1)))
            .Select(order => new { order.CreatedAt, order.CompletedAt })
            .ToListAsync(cancellationToken);

        return Enumerable.Range(0, (endDate - startDate).Days + 1)
            .Select(offset => startDate.AddDays(offset))
            .Select(date => new DailyServiceOrderVolumeResponse(
                date,
                orders.Count(order => order.CreatedAt.Date == date),
                orders.Count(order => order.CompletedAt?.Date == date)))
            .ToList();
    }
}