using System.Collections.Concurrent;
using MechanicsSoftware.Application.Abstractions;
using MechanicsSoftware.Application.Exceptions;

namespace MechanicsSoftware.IntegrationTests.Fixtures;

public sealed class FakeBillingClient : IBillingClient
{
    private readonly ConcurrentDictionary<Guid, BillingBudget> _budgets = new();
    private readonly ConcurrentDictionary<Guid, BillingPayment> _payments = new();

    public bool Unavailable { get; set; }

    public void SetBudget(Guid serviceOrderId, BillingBudget budget) => _budgets[serviceOrderId] = budget;

    public void SetPayment(Guid budgetId, BillingPayment payment) => _payments[budgetId] = payment;

    public Task<BillingBudget?> GetBudgetByServiceOrderAsync(Guid serviceOrderId, CancellationToken cancellationToken = default)
    {
        ThrowIfUnavailable();
        return Task.FromResult(_budgets.GetValueOrDefault(serviceOrderId));
    }

    public Task<BillingPayment?> GetPaymentByBudgetAsync(Guid budgetId, CancellationToken cancellationToken = default)
    {
        ThrowIfUnavailable();
        return Task.FromResult(_payments.GetValueOrDefault(budgetId));
    }

    private void ThrowIfUnavailable()
    {
        if (Unavailable)
            throw new BillingUnavailableException("Billing Service is unreachable.");
    }
}
