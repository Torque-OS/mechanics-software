using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using MechanicsSoftware.Application.UseCases.ServiceOrders;
using MechanicsSoftware.IntegrationTests.Base;
using MechanicsSoftware.IntegrationTests.Fixtures;
using MechanicsSoftware.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using MechanicsSoftware.Domain.Enums;

namespace MechanicsSoftware.IntegrationTests.Features.ServiceOrders;

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

        var budget = await GenerateBudgetAsync(client, orderId);
        budget.TotalInCents.Should().Be(ExpectedBudgetTotalInCents);
        budget.Status.Should().Be("PENDING");

        await SendBudgetAsync(client, orderId);
        await AssertOrderStatusAsync(client, orderId, "AWAITING_APPROVAL");

        await ApproveAsync(client, orderId);
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

        await GenerateBudgetAsync(client, orderId);
        await SendBudgetAsync(client, orderId);
        await AssertOrderStatusAsync(client, orderId, "AWAITING_APPROVAL");

        var rejectResponse = await client.PostAsJsonAsync(
            $"/api/service-orders/{orderId}/budget-decision", new { decision = "reject" });
        rejectResponse.StatusCode.Should().Be(HttpStatusCode.OK);

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
        await GenerateBudgetAsync(client, awaitingId);
        await SendBudgetAsync(client, awaitingId);

        var inExecutionId = await CreateServiceOrderAsync(client, customerId, vehicleId);
        await StartDiagnosisAsync(client, inExecutionId);
        await AddServiceItemAsync(client, inExecutionId, serviceId);
        await GenerateBudgetAsync(client, inExecutionId);
        await SendBudgetAsync(client, inExecutionId);
        await ApproveAsync(client, inExecutionId);

        var deliveredId = await CreateServiceOrderAsync(client, customerId, vehicleId);
        await StartDiagnosisAsync(client, deliveredId);
        await AddServiceItemAsync(client, deliveredId, serviceId);
        await GenerateBudgetAsync(client, deliveredId);
        await SendBudgetAsync(client, deliveredId);
        await ApproveAsync(client, deliveredId);
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
    public async Task BudgetDecision_WithInvalidDecision_ShouldReturn400()
    {
        var client = await AuthenticatedClientAsync();

        var customerId = await CreateCustomerAsync(
            client, document: "52998224725", email: "invalid@example.com", phone: "11977776666");
        var vehicleId = await CreateVehicleAsync(client, customerId, plate: "GHI3F45");
        var serviceId = await CreateServiceAsync(client, name: "Balanceamento");
        var partId = await CreatePartAsync(client, code: "OIL-003", name: "Oleo de motor");

        var orderId = await CreateServiceOrderAsync(client, customerId, vehicleId);
        await StartDiagnosisAsync(client, orderId);
        await AddServiceItemAsync(client, orderId, serviceId);
        await AddPartItemAsync(client, orderId, partId);
        await GenerateBudgetAsync(client, orderId);
        await SendBudgetAsync(client, orderId);

        var response = await client.PostAsJsonAsync(
            $"/api/service-orders/{orderId}/budget-decision", new { decision = "invalid" });
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ========================================================================
    // Helper Methods
    // ========================================================================

    private HttpClient CustomerClient(Guid customerId)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(WebApplicationFactoryFixture.JwtSecret));

        var token = new JwtSecurityToken(
            claims:
            [
                new Claim("customerId", customerId.ToString()),
                new Claim("cpf", "11144477735"),
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

    private static async Task<BudgetDto> GenerateBudgetAsync(HttpClient client, Guid orderId)
    {
        var response = await client.PostAsJsonAsync(
            $"/api/service-orders/{orderId}/budget", new { });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

        return await ReadAsync<BudgetDto>(response);
    }

    private static async Task SendBudgetAsync(HttpClient client, Guid orderId)
    {
        var response = await client.PostAsJsonAsync(
            $"/api/service-orders/{orderId}/send-budget", new { });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    private static async Task ApproveAsync(HttpClient client, Guid orderId)
    {
        var response = await client.PostAsJsonAsync(
            $"/api/service-orders/{orderId}/budget-decision", new { decision = "approve" });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
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
    private sealed record BudgetDto(Guid Id, int TotalInCents, string TotalFormatted, string Status, DateTime CreatedAt);
    private sealed record ServiceOrderStatusDto(Guid Id, string Status, DateTime? DeliveredAt);
    private sealed record ServiceOrderSummaryDto(Guid Id, string Status);
}
