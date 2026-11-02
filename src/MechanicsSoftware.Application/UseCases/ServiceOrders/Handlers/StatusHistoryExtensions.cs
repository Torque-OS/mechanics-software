using MechanicsSoftware.Application.Abstractions;
using MechanicsSoftware.Domain.Entities;

namespace MechanicsSoftware.Application.UseCases.ServiceOrders.Handlers;

internal static class StatusHistoryExtensions
{
    public static Task AppendStatusHistoryAsync(
        this IServiceOrderStatusHistoryRepository? history,
        ServiceOrder order,
        DateTime enteredAt,
        CancellationToken cancellationToken) =>
        history is null
            ? Task.CompletedTask
            : history.AppendAsync(order.Id, order.Status.Value, enteredAt, cancellationToken);
}