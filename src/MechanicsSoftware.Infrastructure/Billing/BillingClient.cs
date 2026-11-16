using System.Net;
using System.Net.Http.Json;
using MechanicsSoftware.Application.Abstractions;
using MechanicsSoftware.Application.Exceptions;
using Polly.CircuitBreaker;
using Polly.Timeout;

namespace MechanicsSoftware.Infrastructure.Billing;

public sealed class BillingClient(HttpClient http) : IBillingClient
{
    public Task<BillingBudget?> GetBudgetByServiceOrderAsync(
        Guid serviceOrderId, CancellationToken cancellationToken = default) =>
        GetOrNullAsync<BillingBudget>($"api/budgets?serviceOrderId={serviceOrderId}", cancellationToken);

    public Task<BillingPayment?> GetPaymentByBudgetAsync(
        Guid budgetId, CancellationToken cancellationToken = default) =>
        GetOrNullAsync<BillingPayment>($"api/payments?budgetId={budgetId}", cancellationToken);

    private async Task<T?> GetOrNullAsync<T>(string path, CancellationToken cancellationToken) where T : class
    {
        HttpResponseMessage response;
        try
        {
            response = await http.GetAsync(new Uri(path, UriKind.Relative), cancellationToken);
        }
        catch (Exception exception) when (exception is HttpRequestException or TimeoutRejectedException
                                              or BrokenCircuitException
                                          || (exception is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            throw new BillingUnavailableException(Describe(exception), exception);
        }

        using (response)
        {
            if (response.StatusCode == HttpStatusCode.NotFound)
                return null;

            if (!response.IsSuccessStatusCode)
                throw new BillingUnavailableException(
                    $"Billing Service answered {(int)response.StatusCode} {response.StatusCode}.");

            return await response.Content.ReadFromJsonAsync<T>(cancellationToken);
        }
    }

    private static string Describe(Exception exception) => exception switch
    {
        BrokenCircuitException => "Billing Service circuit is open after repeated failures.",
        TimeoutRejectedException or TaskCanceledException => "Billing Service did not answer in time.",
        _ => "Billing Service is unreachable.",
    };
}
