using FluentAssertions;
using MechanicsSoftware.Application.Abstractions;
using MechanicsSoftware.Infrastructure.Persistence;
using Testcontainers.MongoDb;

namespace MechanicsSoftware.IntegrationTests.Infrastructure;

public sealed class MongoStatusHistoryStoreTests : IAsyncLifetime, IDisposable
{
    private readonly MongoDbContainer _container =
        new MongoDbBuilder()
            .WithImage("mongo:8")
            .Build();

    private MongoStatusHistoryStore _store = null!;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        _store = new MongoStatusHistoryStore(new MongoStatusHistorySettings
        {
            ConnectionString = _container.GetConnectionString(),
            DatabaseName = $"status_history_test_{Guid.NewGuid():N}",
            CollectionName = "service_order_status_history"
        });
    }

    public async Task DisposeAsync()
    {
        _store.Dispose();
        await _container.StopAsync();
        await _container.DisposeAsync();
    }

    public void Dispose()
    {
    }

    [Fact]
    public async Task AppendAsync_IsIdempotentForDuplicateRecords()
    {
        var serviceOrderId = Guid.NewGuid();
        var enteredAt = DateTime.UtcNow;
        enteredAt = new DateTime(
            enteredAt.Ticks - enteredAt.Ticks % TimeSpan.TicksPerMillisecond,
            DateTimeKind.Utc);
        var record = new ServiceOrderStatusHistoryRecord(
            Guid.NewGuid(),
            serviceOrderId,
            "RECEIVED",
            enteredAt);

        await _store.AppendAsync([record]);
        await _store.AppendAsync([record]);

        var history = await _store.GetAsync(serviceOrderId);

        history.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(record);
    }
}
