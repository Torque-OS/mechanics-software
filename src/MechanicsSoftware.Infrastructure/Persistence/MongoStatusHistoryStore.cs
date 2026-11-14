using MechanicsSoftware.Application.Abstractions;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;

namespace MechanicsSoftware.Infrastructure.Persistence;

public sealed class MongoStatusHistorySettings
{
    public string ConnectionString { get; set; } = string.Empty;
    public string DatabaseName { get; set; } = "mechanics_software";
    public string CollectionName { get; set; } = "service_order_status_history";
}

public sealed class MongoStatusHistoryStore : IServiceOrderStatusHistoryStore, IDisposable
{
    private readonly IMongoCollection<StatusHistoryDocument> _collection;
    private readonly SemaphoreSlim _indexLock = new(1, 1);
    private volatile bool _indexCreated;

    public MongoStatusHistoryStore(MongoStatusHistorySettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.ConnectionString))
            throw new InvalidOperationException("MongoDB connection string is not configured.");

        var client = new MongoClient(settings.ConnectionString);
        var database = client.GetDatabase(settings.DatabaseName);
        _collection = database.GetCollection<StatusHistoryDocument>(settings.CollectionName);
    }

    public async Task AppendAsync(
        IReadOnlyCollection<ServiceOrderStatusHistoryRecord> records,
        CancellationToken cancellationToken = default)
    {
        if (records.Count == 0)
            return;

        await EnsureIndexesAsync(cancellationToken);

        var operations = records.Select(record =>
            new UpdateOneModel<StatusHistoryDocument>(
                Builders<StatusHistoryDocument>.Filter.And(
                    Builders<StatusHistoryDocument>.Filter.Eq(document => document.ServiceOrderId, record.ServiceOrderId),
                    Builders<StatusHistoryDocument>.Filter.Eq(document => document.Status, record.Status),
                    Builders<StatusHistoryDocument>.Filter.Eq(document => document.EnteredAt, record.EnteredAt)),
                Builders<StatusHistoryDocument>.Update
                    .SetOnInsert(document => document.Id, record.Id)
                    .SetOnInsert(document => document.ServiceOrderId, record.ServiceOrderId)
                    .SetOnInsert(document => document.Status, record.Status)
                    .SetOnInsert(document => document.EnteredAt, record.EnteredAt))
            {
                IsUpsert = true
            });

        await _collection.BulkWriteAsync(operations, cancellationToken: cancellationToken);
    }

    public async Task<IReadOnlyList<ServiceOrderStatusHistoryRecord>> GetAsync(
        Guid serviceOrderId,
        CancellationToken cancellationToken = default)
        => await GetAsync([serviceOrderId], cancellationToken);

    public async Task<IReadOnlyList<ServiceOrderStatusHistoryRecord>> GetAsync(
        IReadOnlyCollection<Guid> serviceOrderIds,
        CancellationToken cancellationToken = default)
    {
        if (serviceOrderIds.Count == 0)
            return [];

        await EnsureIndexesAsync(cancellationToken);

        var documents = await _collection.Find(
                Builders<StatusHistoryDocument>.Filter.In(
                    document => document.ServiceOrderId,
                    serviceOrderIds))
            .SortBy(document => document.EnteredAt)
            .ToListAsync(cancellationToken);

        return documents
            .Select(document => new ServiceOrderStatusHistoryRecord(
                document.Id,
                document.ServiceOrderId,
                document.Status,
                document.EnteredAt))
            .ToList();
    }

    private async Task EnsureIndexesAsync(CancellationToken cancellationToken)
    {
        if (_indexCreated)
            return;

        await _indexLock.WaitAsync(cancellationToken);
        try
        {
            if (_indexCreated)
                return;

            await _collection.Indexes.CreateOneAsync(
                new CreateIndexModel<StatusHistoryDocument>(
                    Builders<StatusHistoryDocument>.IndexKeys
                        .Ascending(document => document.ServiceOrderId)
                        .Ascending(document => document.EnteredAt)
                        .Ascending(document => document.Status),
                    new CreateIndexOptions { Unique = true }),
                cancellationToken: cancellationToken);

            _indexCreated = true;
        }
        finally
        {
            _indexLock.Release();
        }
    }

    private sealed class StatusHistoryDocument
    {
        [BsonId]
        public Guid Id { get; set; }
        public Guid ServiceOrderId { get; set; }
        public string Status { get; set; } = string.Empty;
        public DateTime EnteredAt { get; set; }
    }

    public void Dispose() => _indexLock.Dispose();
}
