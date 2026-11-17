using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using MechanicsSoftware.Application.Abstractions;
using MechanicsSoftware.Application.Exceptions;
using MechanicsSoftware.Infrastructure.Billing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;

namespace MechanicsSoftware.UnitTests.Infrastructure;

public sealed class BillingClientTests : IAsyncDisposable
{
    private readonly StubHandler _billing = new();
    private ServiceProvider? _provider;

    private IBillingClient Client(Action<HttpStandardResilienceOptions>? tune = null)
    {
        var services = new ServiceCollection();
        services.AddHttpClient<IBillingClient, BillingClient>(client => client.BaseAddress = new Uri("http://billing.test/"))
            .ConfigurePrimaryHttpMessageHandler(() => _billing)
            .AddStandardResilienceHandler(options =>
            {
                BillingResilience.Configure(options);
                tune?.Invoke(options);
            });
        _provider = services.BuildServiceProvider();
        return _provider.GetRequiredService<IBillingClient>();
    }

    private static HttpResponseMessage Json(object body) =>
        new(HttpStatusCode.OK) { Content = JsonContent.Create(body) };

    private static HttpResponseMessage Status(HttpStatusCode status) => new(status);

    public async ValueTask DisposeAsync()
    {
        if (_provider is not null)
            await _provider.DisposeAsync();
    }

    [Fact]
    public async Task GetBudgetByServiceOrder_Ok_MapsTheBillingResponse()
    {
        var serviceOrderId = Guid.NewGuid();
        var budgetId = Guid.NewGuid();
        _billing.Respond(_ => Json(new
        {
            id = budgetId,
            serviceOrderId,
            customerId = Guid.NewGuid(),
            status = "PENDING",
            totalInCents = 24_000,
            totalFormatted = "R$ 240,00",
            createdAt = DateTime.UtcNow,
            decidedAt = (DateTime?)null,
            items = new[]
            {
                new { id = Guid.NewGuid(), type = "Service", referenceId = Guid.NewGuid(), description = "Oil change", unitPriceInCents = 15_000, quantity = 1, totalInCents = 15_000 },
                new { id = Guid.NewGuid(), type = "Part", referenceId = Guid.NewGuid(), description = "Oil filter", unitPriceInCents = 4_500, quantity = 2, totalInCents = 9_000 },
            },
            cancelledAt = (DateTime?)null,
        }));

        var budget = await Client().GetBudgetByServiceOrderAsync(serviceOrderId);

        budget!.Id.Should().Be(budgetId);
        budget.Status.Should().Be("PENDING");
        budget.TotalInCents.Should().Be(24_000);
        budget.Items.Should().HaveCount(2);
        budget.Items[1].Description.Should().Be("Oil filter");
        _billing.LastRequest!.RequestUri!.PathAndQuery.Should().Be($"/api/budgets?serviceOrderId={serviceOrderId}");
    }

    [Fact]
    public async Task GetPaymentByBudget_Ok_MapsTheBillingResponse()
    {
        var budgetId = Guid.NewGuid();
        _billing.Respond(_ => Json(new { id = Guid.NewGuid(), budgetId, status = "CONFIRMED", amountInCents = 15_000, qrData = "qr", createdAt = DateTime.UtcNow, confirmedAt = DateTime.UtcNow }));

        var payment = await Client().GetPaymentByBudgetAsync(budgetId);

        payment!.Status.Should().Be("CONFIRMED");
        payment.AmountInCents.Should().Be(15_000);
        payment.QrData.Should().Be("qr");
        _billing.LastRequest!.RequestUri!.PathAndQuery.Should().Be($"/api/payments?budgetId={budgetId}");
    }

    [Fact]
    public async Task NotFound_ReturnsNullWithoutRetrying()
    {
        _billing.Respond(_ => Status(HttpStatusCode.NotFound));

        var budget = await Client().GetBudgetByServiceOrderAsync(Guid.NewGuid());

        budget.Should().BeNull();
        _billing.Calls.Should().Be(1);
    }

    [Fact]
    public async Task TransientServerError_IsRetriedUntilItSucceeds()
    {
        _billing.Respond(call => call <= 2
            ? Status(HttpStatusCode.ServiceUnavailable)
            : Json(new { id = Guid.NewGuid(), status = "PENDING", amountInCents = 1, createdAt = DateTime.UtcNow }));

        var payment = await Client().GetPaymentByBudgetAsync(Guid.NewGuid());

        payment.Should().NotBeNull();
        _billing.Calls.Should().Be(3);
    }

    [Fact]
    public async Task PersistentServerError_ThrowsBillingUnavailableAfterTheRetries()
    {
        _billing.Respond(_ => Status(HttpStatusCode.InternalServerError));

        var act = () => Client().GetBudgetByServiceOrderAsync(Guid.NewGuid());

        await act.Should().ThrowAsync<BillingUnavailableException>().WithMessage("*500*");
        _billing.Calls.Should().Be(3);
    }

    [Fact]
    public async Task ClientError_IsNotRetried()
    {
        _billing.Respond(_ => Status(HttpStatusCode.Forbidden));

        var act = () => Client().GetBudgetByServiceOrderAsync(Guid.NewGuid());

        await act.Should().ThrowAsync<BillingUnavailableException>().WithMessage("*403*");
        _billing.Calls.Should().Be(1);
    }

    [Fact]
    public async Task SlowBilling_ThrowsBillingUnavailableOnTimeout()
    {
        _billing.Respond(async (_, cancellationToken) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
            return Status(HttpStatusCode.OK);
        });
        var client = Client(options =>
        {
            options.AttemptTimeout.Timeout = TimeSpan.FromMilliseconds(200);
            options.TotalRequestTimeout.Timeout = TimeSpan.FromMilliseconds(800);
        });

        var act = () => client.GetBudgetByServiceOrderAsync(Guid.NewGuid());

        await act.Should().ThrowAsync<BillingUnavailableException>().WithMessage("*in time*");
    }

    [Fact]
    public async Task Unreachable_ThrowsBillingUnavailable()
    {
        _billing.Respond(_ => throw new HttpRequestException("Connection refused"));

        var act = () => Client().GetBudgetByServiceOrderAsync(Guid.NewGuid());

        await act.Should().ThrowAsync<BillingUnavailableException>().WithMessage("*unreachable*");
    }

    [Fact]
    public async Task RepeatedFailures_OpenTheCircuitAndStopCallingBilling()
    {
        _billing.Respond(_ => Status(HttpStatusCode.ServiceUnavailable));
        var client = Client();

        var opened = false;
        for (var attempt = 0; attempt < 5 && !opened; attempt++)
        {
            var exception = await Record.ExceptionAsync(() => client.GetBudgetByServiceOrderAsync(Guid.NewGuid()));
            opened = exception is BillingUnavailableException { Message: var message } && message.Contains("circuit");
        }

        opened.Should().BeTrue();
        var callsWhenOpen = _billing.Calls;

        var act = () => client.GetBudgetByServiceOrderAsync(Guid.NewGuid());

        await act.Should().ThrowAsync<BillingUnavailableException>().WithMessage("*circuit*");
        _billing.Calls.Should().Be(callsWhenOpen);
    }

    [Fact]
    public void Configure_SetsTimeoutsRetryAndCircuitBreaker()
    {
        var options = new HttpStandardResilienceOptions();

        BillingResilience.Configure(options);

        options.TotalRequestTimeout.Timeout.Should().Be(TimeSpan.FromSeconds(5));
        options.AttemptTimeout.Timeout.Should().Be(TimeSpan.FromSeconds(2));
        options.Retry.MaxRetryAttempts.Should().Be(2);
        options.CircuitBreaker.MinimumThroughput.Should().Be(5);
        options.CircuitBreaker.BreakDuration.Should().Be(TimeSpan.FromSeconds(15));
        options.CircuitBreaker.SamplingDuration.Should().BeGreaterThanOrEqualTo(2 * options.AttemptTimeout.Timeout);
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private Func<int, CancellationToken, Task<HttpResponseMessage>> _respond =
            (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        private int _calls;

        public int Calls => _calls;

        public HttpRequestMessage? LastRequest { get; private set; }

        public void Respond(Func<int, HttpResponseMessage> respond) =>
            _respond = (call, _) => Task.FromResult(respond(call));

        public void Respond(Func<int, CancellationToken, Task<HttpResponseMessage>> respond) => _respond = respond;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return _respond(Interlocked.Increment(ref _calls), cancellationToken);
        }
    }
}
