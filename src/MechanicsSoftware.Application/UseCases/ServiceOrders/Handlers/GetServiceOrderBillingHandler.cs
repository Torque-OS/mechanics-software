using MechanicsSoftware.Application.Abstractions;
using MechanicsSoftware.Application.Exceptions;
using MechanicsSoftware.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MechanicsSoftware.Application.UseCases.ServiceOrders.Handlers;

public sealed partial class GetServiceOrderBillingHandler(
    IAppDbContext db, IBillingClient billing, ILogger<GetServiceOrderBillingHandler> logger)
{
    public async Task<ServiceOrderBillingResponse> ExecuteAsync(
        Guid serviceOrderId, CancellationToken cancellationToken = default)
    {
        if (!await db.ServiceOrders.AnyAsync(o => o.Id == serviceOrderId, cancellationToken))
            throw new NotFoundException(nameof(ServiceOrder), serviceOrderId);

        try
        {
            var budget = await billing.GetBudgetByServiceOrderAsync(serviceOrderId, cancellationToken);
            var payment = budget is null
                ? null
                : await billing.GetPaymentByBudgetAsync(budget.Id, cancellationToken);

            return ServiceOrderBillingResponse.Available(serviceOrderId, budget, payment);
        }
        catch (BillingUnavailableException exception)
        {
            LogBillingUnavailable(logger, serviceOrderId, exception.Message);
            return ServiceOrderBillingResponse.Unavailable(serviceOrderId, exception.Message);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Billing unavailable for service order {ServiceOrderId}: {Reason}")]
    private static partial void LogBillingUnavailable(ILogger logger, Guid serviceOrderId, string reason);
}
