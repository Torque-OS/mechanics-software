using MechanicsSoftware.Application.Abstractions;
using MechanicsSoftware.Application.UseCases.ServiceOrders.Queries;
using MechanicsSoftware.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace MechanicsSoftware.Application.UseCases.ServiceOrders.Handlers;

public sealed class ListServiceOrdersHandler(IAppDbContext db)
{
    public async Task<IReadOnlyList<ServiceOrderSummaryResponse>> ExecuteAsync(
        ListServiceOrdersQuery query, CancellationToken cancellationToken = default)
    {
        var completed = new ServiceOrderStatus(ServiceOrderStatus.Status.Completed);
        var delivered = new ServiceOrderStatus(ServiceOrderStatus.Status.Delivered);
        var inExecution = new ServiceOrderStatus(ServiceOrderStatus.Status.InExecution);
        var awaitingApproval = new ServiceOrderStatus(ServiceOrderStatus.Status.AwaitingApproval);
        var awaitingPayment = new ServiceOrderStatus(ServiceOrderStatus.Status.AwaitingPayment);
        var inDiagnosis = new ServiceOrderStatus(ServiceOrderStatus.Status.InDiagnosis);

        var orders = db.ServiceOrders
            .Where(o => o.Status != completed && o.Status != delivered);

        if (!string.IsNullOrWhiteSpace(query.Status))
        {
            var parsed = ParseStatus(query.Status);
            if (parsed.HasValue)
            {
                var statusVo = new ServiceOrderStatus(parsed.Value);
                orders = orders.Where(o => o.Status == statusVo);
            }
        }

        return await orders
            .OrderBy(o => o.Status == inExecution ? 1
                : o.Status == awaitingApproval ? 2
                : o.Status == awaitingPayment ? 3
                : o.Status == inDiagnosis ? 4 : 5)
            .ThenBy(o => o.CreatedAt)
            .Select(o => new ServiceOrderSummaryResponse(
                o.Id,
                o.CustomerId,
                o.VehicleId,
                o.Status.ToString()!,
                o.CreatedAt))
            .ToListAsync(cancellationToken);
    }

    private static ServiceOrderStatus.Status? ParseStatus(string value)
    {
        var normalized = value.Replace("_", "");
        return Enum.TryParse<ServiceOrderStatus.Status>(normalized, ignoreCase: true, out var result)
            ? result
            : null;
    }
}
