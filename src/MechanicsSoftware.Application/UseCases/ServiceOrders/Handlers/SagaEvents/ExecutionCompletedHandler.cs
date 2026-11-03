using MechanicsSoftware.Application.Abstractions;
using MechanicsSoftware.Domain.ValueObjects;

namespace MechanicsSoftware.Application.UseCases.ServiceOrders.Handlers.SagaEvents;

/// <summary>
/// Reacts to the <c>ExecutionCompleted</c> event published by the Execution Service (ADR-011): the
/// repair is finished, so the order moves to COMPLETED through the same use case as the staff
/// endpoint (part usage, metrics). Idempotent: a redelivered event finds the order already completed.
/// </summary>
public sealed class ExecutionCompletedHandler(IAppDbContext db, CompleteServiceOrderHandler completeHandler)
{
    public async Task<ServiceOrderResponse> ExecuteAsync(
        Guid serviceOrderId, CancellationToken cancellationToken = default)
    {
        var order = await db.ServiceOrders.FindFullAsync(serviceOrderId, cancellationToken);

        if (order.Status.Is(ServiceOrderStatus.Status.Completed) ||
            order.Status.Is(ServiceOrderStatus.Status.Delivered))
            return ServiceOrderResponse.From(order);

        return await completeHandler.ExecuteAsync(serviceOrderId, cancellationToken);
    }
}
