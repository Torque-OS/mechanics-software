using MechanicsSoftware.Application.Abstractions;
using MechanicsSoftware.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace MechanicsSoftware.Application.UseCases.ServiceOrders.Handlers.SagaEvents;

/// <summary>
/// Reacts to the <c>BudgetGenerated</c> event published by the Billing Service (ADR-011).
/// Will be invoked by a MassTransit consumer once messaging is wired (F4-13); exposed here as a
/// plain handler so the OS Service domain and persistence side of the saga can be built and
/// tested independently of the broker (F4-08).
/// </summary>
public sealed class BudgetGeneratedHandler(IAppDbContext db, IEmailNotifier emailNotifier, ILogger<BudgetGeneratedHandler> logger)
{
    public async Task<ServiceOrderResponse> ExecuteAsync(
        Guid serviceOrderId, Guid budgetId, int budgetTotalInCents, CancellationToken cancellationToken = default)
    {
        var order = await db.ServiceOrders.FindFullAsync(serviceOrderId, cancellationToken);

        order.AttachBudget(budgetId, new Money(budgetTotalInCents));

        await db.SaveChangesAsync(cancellationToken);

        await emailNotifier.TrySendStatusEmailAsync(db, logger, order, cancellationToken);

        return ServiceOrderResponse.From(order);
    }
}
