using MechanicsSoftware.Application.Abstractions;
using MechanicsSoftware.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace MechanicsSoftware.Application.UseCases.ServiceOrders.Handlers.SagaEvents;

/// <summary>
/// Reacts to the <c>BudgetGenerated</c> event published by the Billing Service (ADR-011), invoked
/// by <c>BudgetGeneratedConsumer</c> (F4-12). Kept as a plain handler so the saga side of the OS
/// Service can be tested without the broker.
/// </summary>
public sealed class BudgetGeneratedHandler(IAppDbContext db, IEmailNotifier emailNotifier, ILogger<BudgetGeneratedHandler> logger)
{
    public async Task<ServiceOrderResponse> ExecuteAsync(
        Guid serviceOrderId, Guid budgetId, int budgetTotalInCents, CancellationToken cancellationToken = default)
    {
        var order = await db.ServiceOrders.FindFullAsync(serviceOrderId, cancellationToken);

        // Idempotent: a redelivered or duplicated event finds this budget already attached.
        if (order.BudgetId == budgetId)
            return ServiceOrderResponse.From(order);

        order.AttachBudget(budgetId, new Money(budgetTotalInCents));

        await db.SaveChangesAsync(cancellationToken);

        await emailNotifier.TrySendStatusEmailAsync(db, logger, order, cancellationToken);

        return ServiceOrderResponse.From(order);
    }
}
