using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using MechanicsSoftware.Application.UseCases.ServiceOrders;
using MechanicsSoftware.Application.UseCases.ServiceOrders.Handlers.SagaEvents;
using MechanicsSoftware.IntegrationTests.Base;
using MechanicsSoftware.IntegrationTests.Fixtures;
using MechanicsSoftware.Infrastructure.Persistence;
using MechanicsSoftware.Infrastructure.Security;
using MassTransit.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using MechanicsSoftware.Domain.Authorization;
using MechanicsSoftware.Domain.Enums;
using MassTransit;
using TorqueOS.Contracts.Billing;
using TorqueOS.Contracts.Execution;
using TorqueOS.Contracts.ServiceOrders;

namespace MechanicsSoftware.IntegrationTests.Features.ServiceOrders;

// Budget generation, sending and approval/rejection are now owned by the Billing Service
// (ADR-010) and reach the OS Service as saga events (ADR-011). Most flow tests simulate those
// events by invoking the saga-event handlers through the app's own DI container; the
// "*ThroughBroker" tests publish the real contracts on the MassTransit test harness instead, so the
// consumers (F4-12) are exercised end to end. Everything else goes through real HTTP calls.
[Collection("IntegrationTests")]
public sealed class ServiceOrderFlowTests : IntegrationTestBase
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private const string AdminEmail = "admin@mechanics.local";
    private const string AdminPassword = "Admin@123";

    private const int ServicePriceInCents = 12_000;
    private const int PartPriceInCents = 5_000;
    private const int PartInitialStock = 10;
    private const int PartItemQuantity = 2;
    private const int ServiceItemQuantity = 1;

    private static readonly int ExpectedBudgetTotalInCents =
        ServicePriceInCents * ServiceItemQuantity + PartPriceInCents * PartItemQuantity;

    public ServiceOrderFlowTests(WebApplicationFactoryFixture factory) : base(factory)
    {
    }

    public override async Task InitializeAsync()
    {
        await Factory.ResetDomainDataAsync();
        await AuthenticateAsync(AdminEmail, AdminPassword);
    }

    [Fact]
    public async Task ApprovalFlow_EndToEnd_ShouldTransitionStatusesAndMoveStock()
    {
        var client = await AuthenticatedClientAsync();

        var customerId = await CreateCustomerAsync(client);
        var vehicleId = await CreateVehicleAsync(client, customerId);
        var serviceId = await CreateServiceAsync(client);
        var partId = await CreatePartAsync(client);

        var orderId = await CreateServiceOrderAsync(client, customerId, vehicleId);

        await AssertOrderStatusAsync(client, orderId, "RECEIVED");

        await StartDiagnosisAsync(client, orderId);
        await AssertOrderStatusAsync(client, orderId, "IN_DIAGNOSIS");

        await AddServiceItemAsync(client, orderId, serviceId);
        await AddPartItemAsync(client, orderId, partId);

        await AssertStockAsync(partId,
            expectedStock: PartInitialStock,
            expectedReserved: PartItemQuantity);

        await AssertMovementExistsAsync(partId, StockMovementType.Reservation,
            PartItemQuantity, reference: orderId);

        var afterBudget = await SimulateBudgetGeneratedAsync(orderId, ExpectedBudgetTotalInCents);
        afterBudget.BudgetTotalInCents.Should().Be(ExpectedBudgetTotalInCents);
        afterBudget.Status.Should().Be("AWAITING_APPROVAL");

        await SimulateBudgetApprovedAsync(orderId);
        await AssertOrderStatusAsync(client, orderId, "AWAITING_PAYMENT");

        await SimulatePaymentConfirmedAsync(orderId);
        await AssertOrderStatusAsync(client, orderId, "IN_EXECUTION");

        await StartExecutionAsync(client, orderId);
        await AssertOrderStatusAsync(client, orderId, "IN_EXECUTION");

        await CompleteAsync(client, orderId);
        await AssertOrderStatusAsync(client, orderId, "COMPLETED");

        await AssertStockAsync(partId,
            expectedStock: PartInitialStock - PartItemQuantity,
            expectedReserved: 0);

        await AssertMovementExistsAsync(partId, StockMovementType.Outbound,
            PartItemQuantity, reference: orderId);

        await DeliverAsync(client, orderId);

        using var anonymousClient = Factory.CreateClient();
        var anonymousResponse = await anonymousClient.GetAsync($"/api/service-orders/{orderId}/status");
        anonymousResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        using var customerClient = CustomerClient(customerId);
        var customerResponse = await customerClient.GetAsync($"/api/service-orders/{orderId}/status");
        customerResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var publicStatus = await ReadAsync<ServiceOrderStatusDto>(customerResponse);
        publicStatus.Id.Should().Be(orderId);
        publicStatus.Status.Should().Be("DELIVERED");
        publicStatus.DeliveredAt.Should().NotBeNull();
    }

    [Fact]
    public async Task RejectionFlow_ShouldCancelOrderAndReleaseStockReservations()
    {
        var client = await AuthenticatedClientAsync();

        var customerId = await CreateCustomerAsync(
            client, document: "52998224725", email: "reject@example.com", phone: "11988887777");
        var vehicleId = await CreateVehicleAsync(client, customerId, plate: "DEF2E34");
        var serviceId = await CreateServiceAsync(client, name: "Alinhamento");
        var partId = await CreatePartAsync(client, code: "BRK-002", name: "Pastilha de freio");

        var orderId = await CreateServiceOrderAsync(client, customerId, vehicleId);

        await StartDiagnosisAsync(client, orderId);
        await AddServiceItemAsync(client, orderId, serviceId);
        await AddPartItemAsync(client, orderId, partId);

        await AssertStockAsync(partId,
            expectedStock: PartInitialStock,
            expectedReserved: PartItemQuantity);

        await SimulateBudgetGeneratedAsync(orderId, ExpectedBudgetTotalInCents);
        await AssertOrderStatusAsync(client, orderId, "AWAITING_APPROVAL");

        // Compensation: BudgetRejected saga event (ADR-011).
        await SimulateBudgetRejectedAsync(orderId);

        await AssertOrderStatusAsync(client, orderId, "CANCELLED");

        await AssertStockAsync(partId,
            expectedStock: PartInitialStock,
            expectedReserved: 0);

        await AssertMovementExistsAsync(partId, StockMovementType.Release,
            PartItemQuantity, reference: orderId);

        await AssertMovementDoesNotExistAsync(partId, StockMovementType.Outbound);
    }

    [Fact]
    public async Task List_ReturnsActiveOrdersOrderedByStatusPriority()
    {
        var client = await AuthenticatedClientAsync();

        var customerId = await CreateCustomerAsync(
            client, document: "98765432100", email: "list@example.com", phone: "11911112222");
        var vehicleId = await CreateVehicleAsync(client, customerId, plate: "LST1T01");
        var serviceId = await CreateServiceAsync(client, name: "Servico de listagem");

        var receivedId = await CreateServiceOrderAsync(client, customerId, vehicleId);

        var inDiagnosisId = await CreateServiceOrderAsync(client, customerId, vehicleId);
        await StartDiagnosisAsync(client, inDiagnosisId);

        var awaitingId = await CreateServiceOrderAsync(client, customerId, vehicleId);
        await StartDiagnosisAsync(client, awaitingId);
        await AddServiceItemAsync(client, awaitingId, serviceId);
        await SimulateBudgetGeneratedAsync(awaitingId, ServicePriceInCents);

        var inExecutionId = await CreateServiceOrderAsync(client, customerId, vehicleId);
        await StartDiagnosisAsync(client, inExecutionId);
        await AddServiceItemAsync(client, inExecutionId, serviceId);
        await SimulateBudgetGeneratedAsync(inExecutionId, ServicePriceInCents);
        await SimulateBudgetApprovedAsync(inExecutionId);
        await SimulatePaymentConfirmedAsync(inExecutionId);

        var deliveredId = await CreateServiceOrderAsync(client, customerId, vehicleId);
        await StartDiagnosisAsync(client, deliveredId);
        await AddServiceItemAsync(client, deliveredId, serviceId);
        await SimulateBudgetGeneratedAsync(deliveredId, ServicePriceInCents);
        await SimulateBudgetApprovedAsync(deliveredId);
        await SimulatePaymentConfirmedAsync(deliveredId);
        await StartExecutionAsync(client, deliveredId);
        await CompleteAsync(client, deliveredId);
        await DeliverAsync(client, deliveredId);

        var response = await client.GetAsync("/api/service-orders");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var orders = await ReadAsync<List<ServiceOrderSummaryDto>>(response);

        orders.Should().HaveCount(4);
        orders.Should().NotContain(o => o.Id == deliveredId);
        orders[0].Status.Should().Be("IN_EXECUTION");
        orders[1].Status.Should().Be("AWAITING_APPROVAL");
        orders[2].Status.Should().Be("IN_DIAGNOSIS");
        orders[3].Status.Should().Be("RECEIVED");
    }

    [Fact]
    public async Task Create_PublishesServiceOrderOpenedThroughOutbox()
    {
        var client = await AuthenticatedClientAsync();

        var customerId = await CreateCustomerAsync(client);
        var vehicleId = await CreateVehicleAsync(client, customerId);

        var orderId = await CreateServiceOrderAsync(client, customerId, vehicleId);

        var harness = Factory.Services.GetTestHarness();
        (await harness.Published.Any<ServiceOrderOpened>(m => m.Context.Message.ServiceOrderId == orderId))
            .Should().BeTrue("the outbox must deliver ServiceOrderOpened after the order is saved");

        var published = harness.Published
            .Select<ServiceOrderOpened>(m => m.Context.Message.ServiceOrderId == orderId)
            .Single()
            .Context.Message;

        published.CustomerId.Should().Be(customerId);
        published.VehicleId.Should().Be(vehicleId);
        published.OpenedAt.Kind.Should().Be(DateTimeKind.Utc);
    }

    [Fact]
    public async Task HappyPath_ThroughBroker_DrivesOrderToCompletedAndRecordsHistory()
    {
        var client = await AuthenticatedClientAsync();
        var orderId = await OrderInDiagnosisWithServiceAsync(client);
        var bus = Factory.Services.GetTestHarness().Bus;
        var budgetId = Guid.NewGuid();

        await bus.Publish(new BudgetGenerated(orderId, budgetId, ServicePriceInCents, DateTime.UtcNow));
        await WaitForStatusAsync(client, orderId, "AWAITING_APPROVAL");

        await bus.Publish(new BudgetApproved(orderId, budgetId, DateTime.UtcNow));
        await WaitForStatusAsync(client, orderId, "AWAITING_PAYMENT");

        await bus.Publish(new PaymentConfirmed(orderId, Guid.NewGuid(), budgetId, ServicePriceInCents, DateTime.UtcNow));
        await WaitForStatusAsync(client, orderId, "IN_EXECUTION");

        await bus.Publish(new ExecutionCompleted(orderId, Guid.NewGuid(), DateTime.UtcNow));
        await WaitForStatusAsync(client, orderId, "COMPLETED");

        var response = await client.GetAsync($"/api/service-orders/{orderId}/history");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var history = await ReadAsync<ServiceOrderHistoryResponse>(response);

        history.CurrentStatus.Should().Be("COMPLETED");
        history.History.Select(entry => entry.Status).Should().Equal(
            "RECEIVED", "IN_DIAGNOSIS", "AWAITING_APPROVAL", "AWAITING_PAYMENT", "IN_EXECUTION", "COMPLETED");
    }

    [Fact]
    public async Task BudgetFailed_ThroughBroker_CancelsOrderInDiagnosis()
    {
        var client = await AuthenticatedClientAsync();
        var orderId = await OrderInDiagnosisWithServiceAsync(client);

        await Factory.Services.GetTestHarness().Bus
            .Publish(new BudgetFailed(orderId, "A budget requires at least one service item.", DateTime.UtcNow));

        await WaitForStatusAsync(client, orderId, "CANCELLED");
    }

    [Fact]
    public async Task DuplicatePaymentConfirmed_ThroughBroker_IsIgnoredWithoutFault()
    {
        var client = await AuthenticatedClientAsync();
        var orderId = await OrderInDiagnosisWithServiceAsync(client);
        var harness = Factory.Services.GetTestHarness();
        var budgetId = Guid.NewGuid();

        await harness.Bus.Publish(new BudgetGenerated(orderId, budgetId, ServicePriceInCents, DateTime.UtcNow));
        await WaitForStatusAsync(client, orderId, "AWAITING_APPROVAL");
        await harness.Bus.Publish(new BudgetApproved(orderId, budgetId, DateTime.UtcNow));
        await WaitForStatusAsync(client, orderId, "AWAITING_PAYMENT");

        // Two distinct messages for the same payment, as a racing webhook + verify could produce. They
        // are consumed concurrently: one wins, the other is retried and hits the idempotency guard.
        await harness.Bus.Publish(new PaymentConfirmed(orderId, Guid.NewGuid(), budgetId, ServicePriceInCents, DateTime.UtcNow));
        await harness.Bus.Publish(new PaymentConfirmed(orderId, Guid.NewGuid(), budgetId, ServicePriceInCents, DateTime.UtcNow));

        await WaitForStatusAsync(client, orderId, "IN_EXECUTION");
        (await harness.Consumed.Any<PaymentConfirmed>(m => m.Context.Message.ServiceOrderId == orderId)).Should().BeTrue();
        (await harness.Published.Any<Fault<PaymentConfirmed>>(m => m.Context.Message.Message.ServiceOrderId == orderId))
            .Should().BeFalse("a repeated event must be ignored, not sent to the error queue");
    }

    [Fact]
    public async Task ExecutionFailed_ThroughBroker_CancelsOrderInExecution()
    {
        var client = await AuthenticatedClientAsync();
        var orderId = await OrderInDiagnosisWithServiceAsync(client);
        var bus = Factory.Services.GetTestHarness().Bus;
        var budgetId = Guid.NewGuid();

        await bus.Publish(new BudgetGenerated(orderId, budgetId, ServicePriceInCents, DateTime.UtcNow));
        await WaitForStatusAsync(client, orderId, "AWAITING_APPROVAL");
        await bus.Publish(new BudgetApproved(orderId, budgetId, DateTime.UtcNow));
        await WaitForStatusAsync(client, orderId, "AWAITING_PAYMENT");
        await bus.Publish(new PaymentConfirmed(orderId, Guid.NewGuid(), budgetId, ServicePriceInCents, DateTime.UtcNow));
        await WaitForStatusAsync(client, orderId, "IN_EXECUTION");

        await bus.Publish(new ExecutionFailed(orderId, Guid.NewGuid(), "Part unavailable.", DateTime.UtcNow));

        await WaitForStatusAsync(client, orderId, "CANCELLED");
    }

    // ========================================================================
    // Helper Methods
    // ========================================================================

    private HttpClient CustomerClient(Guid customerId)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(WebApplicationFactoryFixture.JwtSecret));

        var token = new JwtSecurityToken(
            issuer: JwtSettings.DefaultIssuer,
            audience: JwtSettings.DefaultAudience,
            claims:
            [
                new Claim(JwtRegisteredClaimNames.Sub, customerId.ToString()),
                new Claim("cpf", "11144477735"),
                new Claim(JwtSettings.RoleClaimType, Policies.CustomerRole),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            ],
            expires: DateTime.UtcNow.AddMinutes(15),
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));

        var client = Factory.CreateClient();

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", new JwtSecurityTokenHandler().WriteToken(token));

        return client;
    }

    private async Task<HttpClient> AuthenticatedClientAsync()
    {
        var client = Factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/login",
            new { email = AdminEmail, password = AdminPassword });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

        var login = await ReadAsync<LoginDto>(response);
        login.Token.Should().NotBeNullOrWhiteSpace();

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.Token);
        return client;
    }

    private static async Task<Guid> CreateCustomerAsync(
        HttpClient client,
        string document = "11144477735",
        string email = "approve@example.com",
        string phone = "11999990000")
    {
        var response = await client.PostAsJsonAsync("/api/customers", new
        {
            name = "Cliente Teste",
            documentValue = document,
            personType = PersonType.INDIVIDUAL,
            email,
            phone
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());

        var result = await ReadAsync<dynamic>(response);
        return Guid.Parse(result.GetProperty("id").GetString()!);
    }

    private static async Task<Guid> CreateVehicleAsync(
        HttpClient client, Guid customerId, string plate = "ABC1D23")
    {
        var response = await client.PostAsJsonAsync("/api/vehicles", new
        {
            customerId,
            licensePlate = plate,
            make = "Toyota",
            model = "Corolla",
            year = 2020
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());

        var result = await ReadAsync<dynamic>(response);
        return Guid.Parse(result.GetProperty("id").GetString()!);
    }

    private static async Task<Guid> CreateServiceAsync(
        HttpClient client, string name = "Troca de oleo")
    {
        var response = await client.PostAsJsonAsync("/api/services", new
        {
            name,
            description = "Servico padrao",
            basePriceInCents = ServicePriceInCents,
            estimatedMinutes = 60
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());

        var result = await ReadAsync<dynamic>(response);
        return Guid.Parse(result.GetProperty("id").GetString()!);
    }

    private static async Task<Guid> CreatePartAsync(
        HttpClient client, string code = "FLT-001", string name = "Filtro de oleo")
    {
        var response = await client.PostAsJsonAsync("/api/parts", new
        {
            code,
            name,
            description = "Peca de reposicao",
            unitPriceInCents = PartPriceInCents,
            initialStock = PartInitialStock
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());

        var result = await ReadAsync<dynamic>(response);
        return Guid.Parse(result.GetProperty("id").GetString()!);
    }

    private static async Task<Guid> CreateServiceOrderAsync(
        HttpClient client, Guid customerId, Guid vehicleId)
    {
        var response = await client.PostAsJsonAsync("/api/service-orders", new
        {
            customerId,
            vehicleId
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());

        var result = await ReadAsync<dynamic>(response);
        return Guid.Parse(result.GetProperty("id").GetString()!);
    }

    private static async Task StartDiagnosisAsync(HttpClient client, Guid orderId)
    {
        var response = await client.PostAsJsonAsync(
            $"/api/service-orders/{orderId}/start-diagnosis", new { });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    private static async Task AddServiceItemAsync(HttpClient client, Guid orderId, Guid serviceId)
    {
        var response = await client.PostAsJsonAsync(
            $"/api/service-orders/{orderId}/services", new { serviceId, quantity = ServiceItemQuantity });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    private static async Task AddPartItemAsync(HttpClient client, Guid orderId, Guid partId)
    {
        var response = await client.PostAsJsonAsync(
            $"/api/service-orders/{orderId}/parts", new { partId, quantity = PartItemQuantity });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    // Invokes the same handlers as the BudgetGenerated/BudgetApproved/PaymentConfirmed/BudgetRejected
    // consumers (F4-12) directly, for tests that are not about the messaging itself.
    private async Task<ServiceOrderResponse> SimulateBudgetGeneratedAsync(Guid orderId, int totalInCents)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var handler = scope.ServiceProvider.GetRequiredService<BudgetGeneratedHandler>();
        return await handler.ExecuteAsync(orderId, Guid.NewGuid(), totalInCents);
    }

    private async Task SimulateBudgetApprovedAsync(Guid orderId)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var handler = scope.ServiceProvider.GetRequiredService<BudgetApprovedHandler>();
        await handler.ExecuteAsync(orderId);
    }

    private async Task SimulateBudgetRejectedAsync(Guid orderId)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var handler = scope.ServiceProvider.GetRequiredService<BudgetRejectedHandler>();
        await handler.ExecuteAsync(orderId);
    }

    private async Task SimulatePaymentConfirmedAsync(Guid orderId)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var handler = scope.ServiceProvider.GetRequiredService<PaymentConfirmedHandler>();
        await handler.ExecuteAsync(orderId);
    }

    private static async Task StartExecutionAsync(HttpClient client, Guid orderId)
    {
        var response = await client.PostAsJsonAsync(
            $"/api/service-orders/{orderId}/start-execution", new { });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    private static async Task CompleteAsync(HttpClient client, Guid orderId)
    {
        var response = await client.PostAsJsonAsync(
            $"/api/service-orders/{orderId}/complete", new { });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    private static async Task DeliverAsync(HttpClient client, Guid orderId)
    {
        var response = await client.PostAsJsonAsync(
            $"/api/service-orders/{orderId}/deliver", new { });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    private static async Task<Guid> OrderInDiagnosisWithServiceAsync(HttpClient client)
    {
        var customerId = await CreateCustomerAsync(client);
        var vehicleId = await CreateVehicleAsync(client, customerId);
        var serviceId = await CreateServiceAsync(client);
        var orderId = await CreateServiceOrderAsync(client, customerId, vehicleId);

        await StartDiagnosisAsync(client, orderId);
        await AddServiceItemAsync(client, orderId, serviceId);

        return orderId;
    }

    // Consumers run asynchronously after Publish returns, so the status is polled until it changes.
    private static async Task WaitForStatusAsync(HttpClient client, Guid orderId, string expectedStatus)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        string? current = null;

        while (DateTime.UtcNow < deadline)
        {
            var response = await client.GetAsync($"/api/service-orders/{orderId}/status");
            current = (await ReadAsync<ServiceOrderStatusDto>(response)).Status;
            if (current == expectedStatus)
                return;

            await Task.Delay(100);
        }

        current.Should().Be(expectedStatus, "the consumer should have applied the saga event");
    }

    private static async Task AssertOrderStatusAsync(
        HttpClient client, Guid orderId, string expectedStatus)
    {
        var response = await client.GetAsync($"/api/service-orders/{orderId}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var order = await ReadAsync<ServiceOrderResponse>(response);
        order.Status.Should().Be(expectedStatus);
    }

    private async Task AssertStockAsync(Guid partId, int expectedStock, int expectedReserved)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var part = await db.Parts.FindAsync(partId);
        part.Should().NotBeNull();
        part!.StockQuantity.Should().Be(expectedStock);
        part.ReservedQuantity.Should().Be(expectedReserved);
    }

    private async Task AssertMovementExistsAsync(
        Guid partId, StockMovementType type, int quantity, Guid reference)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var part = await db.Parts.FindAsync(partId);
        part.Should().NotBeNull();

        var movement = part!.Movements.FirstOrDefault(m =>
            m.Type == type && m.Quantity == quantity && m.Reference == reference);

        movement.Should().NotBeNull();
    }

    private async Task AssertMovementDoesNotExistAsync(Guid partId, StockMovementType type)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var part = await db.Parts.FindAsync(partId);
        part.Should().NotBeNull();

        var movement = part!.Movements.FirstOrDefault(m => m.Type == type);
        movement.Should().BeNull();
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response)
    {
        var content = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<T>(content, JsonOptions)
            ?? throw new InvalidOperationException("Failed to deserialize response");
    }

    private sealed record LoginDto(string Token);
    private sealed record ServiceOrderStatusDto(Guid Id, string Status, DateTime? DeliveredAt);
    private sealed record ServiceOrderSummaryDto(Guid Id, string Status);
}
