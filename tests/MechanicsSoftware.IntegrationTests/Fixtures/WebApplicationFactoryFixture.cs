using MassTransit;
using MechanicsSoftware.Application.Abstractions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MechanicsSoftware.Infrastructure.Persistence;
using MechanicsSoftware.Application.Abstractions;
using Testcontainers.PostgreSql;

namespace MechanicsSoftware.IntegrationTests.Fixtures;

public sealed class WebApplicationFactoryFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly TestStatusHistoryStore _historyStore = new();
    private readonly PostgreSqlContainer _container =
        new PostgreSqlBuilder()
            .WithImage("postgres:17-alpine")
            .WithDatabase("mechanics_software_test")
            .WithUsername("postgres")
            .WithPassword("postgres")
            .Build();

    public const string JwtSecret = "test-secret-key-for-testing-only-min-32-chars-required-here";

    public FakeBillingClient Billing { get; } = new();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        _ = Server;
    }

    public new async Task DisposeAsync()
    {
        await base.DisposeAsync();
        await _container.StopAsync();
        await _container.DisposeAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            var descriptor = services.FirstOrDefault(d =>
                d.ServiceType == typeof(DbContextOptions<AppDbContext>));
            if (descriptor != null)
                services.Remove(descriptor);

            services.AddDbContext<AppDbContext>(options =>
                options.UseNpgsql(_container.GetConnectionString()));

            var historyDescriptor = services.FirstOrDefault(d =>
                d.ServiceType == typeof(IServiceOrderStatusHistoryStore));
            if (historyDescriptor != null)
                services.Remove(historyDescriptor);
            services.AddSingleton<IServiceOrderStatusHistoryStore>(_historyStore);
        });

        builder.ConfigureTestServices(services =>
        {
            services.AddMassTransitTestHarness();
            services.RemoveAll<IBillingClient>();
            services.AddSingleton<IBillingClient>(Billing);
        });

        builder.UseEnvironment("Test");

        Environment.SetEnvironmentVariable("JWT_SECRET", JwtSecret);
        Environment.SetEnvironmentVariable("JWT_EXPIRATION_MINUTES", "60");
        Environment.SetEnvironmentVariable("SMTP_HOST", "localhost");
        Environment.SetEnvironmentVariable("SMTP_PORT", "1025");
        Environment.SetEnvironmentVariable("SMTP_USER", "test@example.com");
        Environment.SetEnvironmentVariable("SMTP_PASS", "test-password");
        Environment.SetEnvironmentVariable("SMTP_FROM", "noreply@mechanics-software.test");
        Environment.SetEnvironmentVariable("RABBITMQ_HOST", "localhost");
        Environment.SetEnvironmentVariable("RABBITMQ_USERNAME", "test");
        Environment.SetEnvironmentVariable("RABBITMQ_PASSWORD", "test");
        Environment.SetEnvironmentVariable("BILLING_API_URL", "http://billing.test");
    }

    public async Task ResetDatabaseAsync()
    {
        await using var scope = Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        await context.Database.EnsureDeletedAsync();
        await context.Database.MigrateAsync();
        _historyStore.Clear();
    }

    /// <summary>
    /// Truncates all domain tables while preserving the users/admin seed.
    /// Call this in IAsyncLifetime.InitializeAsync() to isolate tests that share this factory.
    /// </summary>
    public async Task ResetDomainDataAsync()
    {
        await using var scope = Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        await context.Database.ExecuteSqlRawAsync("""
            TRUNCATE TABLE
                stock_movements,
                part_items,
                service_items,
                service_orders,
                vehicles,
                customers,
                services,
                parts
            CASCADE
            """);
    }
}
