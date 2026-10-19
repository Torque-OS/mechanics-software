using MechanicsSoftware.Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace MechanicsSoftware.Application.UseCases.ServiceOrders.Handlers.SagaEvents;

/// <summary>
/// Reacts to the <c>BudgetApproved</c> event published by the Billing Service once the customer
/// approves the budget (ADR-011). See <see cref="BudgetGeneratedHandler"/> for the wiring note.
/// </summary>
public sealed class BudgetApprovedHandler(IAppDbContext db, IEmailNotifier emailNotifier, ILogger<BudgetApprovedHandler> logger)
{
    public async Task<ServiceOrderResponse> ExecuteAsync(
        Guid serviceOrderId, CancellationToken cancellationToken = default)
    {
        var order = await db.ServiceOrders.FindFullAsync(serviceOrderId, cancellationToken);

        order.ConfirmBudgetApproval();

        await db.SaveChangesAsync(cancellationToken);

        await emailNotifier.TrySendStatusEmailAsync(db, logger, order, cancellationToken);

        return ServiceOrderResponse.From(order);
    }
}
