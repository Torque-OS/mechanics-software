using FluentAssertions;
using MechanicsSoftware.Application.Abstractions;
using MechanicsSoftware.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Testcontainers.MongoDb;
using Testcontainers.PostgreSql;

namespace MechanicsSoftware.IntegrationTests.Infrastructure;

public sealed class LegacySqlStatusHistoryMigratorTests : IAsyncLifetime, IDisposable
{
    private readonly MongoDbContainer _mongoContainer =
        new MongoDbBuilder()
            .WithImage("mongo:8")
            .Build();

    private readonly PostgreSqlContainer _postgresContainer =
        new PostgreSqlBuilder()
            .WithImage("postgres:17-alpine")
            .WithDatabase("mechanics_software_test")
            .WithUsername("postgres")
            .WithPassword("postgres")
            .Build();

    private MongoStatusHistoryStore _historyStore = null!;

    public async Task InitializeAsync()
    {
        await Task.WhenAll(_mongoContainer.StartAsync(), _postgresContainer.StartAsync());
        _historyStore = new MongoStatusHistoryStore(new MongoStatusHistorySettings
        {
            ConnectionString = _mongoContainer.GetConnectionString(),
            DatabaseName = $"status_history_migration_test_{Guid.NewGuid():N}",
            CollectionName = "service_order_status_history"
        });
    }

    public async Task DisposeAsync()
    {
        _historyStore.Dispose();
        await Task.WhenAll(_mongoContainer.DisposeAsync().AsTask(), _postgresContainer.DisposeAsync().AsTask());
    }

    public void Dispose()
    {
    }

    [Fact]
    public async Task MigrateAsync_ConvertsLegacyEnumNamesAndSkipsUnknownStatuses()
    {
        var serviceOrderId = Guid.NewGuid();
        var unknownStatusOrderId = Guid.NewGuid();
        var recordId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var enteredAt = new DateTime(
            now.Ticks - now.Ticks % TimeSpan.TicksPerMillisecond,
            DateTimeKind.Utc);

        await using (var connection = new NpgsqlConnection(_postgresContainer.GetConnectionString()))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE service_order_status_history_legacy (
                    id uuid NOT NULL,
                    service_order_id uuid NOT NULL,
                    status text NOT NULL,
                    entered_at timestamp with time zone NOT NULL
                );
                """;
            await command.ExecuteNonQueryAsync();

            await using var insertCommand = connection.CreateCommand();
            insertCommand.CommandText = """
                INSERT INTO service_order_status_history_legacy
                    (id, service_order_id, status, entered_at)
                VALUES ($1, $2, 'InDiagnosis', $3), ($4, $5, 'NotAStatus', $3);
                """;
            insertCommand.Parameters.AddWithValue(recordId);
            insertCommand.Parameters.AddWithValue(serviceOrderId);
            insertCommand.Parameters.AddWithValue(enteredAt);
            insertCommand.Parameters.AddWithValue(Guid.NewGuid());
            insertCommand.Parameters.AddWithValue(unknownStatusOrderId);
            await insertCommand.ExecuteNonQueryAsync();
        }

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_postgresContainer.GetConnectionString())
            .Options;
        await using var db = new AppDbContext(options);
        var migrator = new LegacySqlStatusHistoryMigrator(
            db, _historyStore, NullLogger<LegacySqlStatusHistoryMigrator>.Instance);

        await migrator.MigrateAsync();

        var history = await _historyStore.GetAsync(serviceOrderId);
        history.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(
                new ServiceOrderStatusHistoryRecord(
                    recordId,
                    serviceOrderId,
                    "IN_DIAGNOSIS",
                    enteredAt));
        (await _historyStore.GetAsync(unknownStatusOrderId)).Should().BeEmpty();
    }
}
