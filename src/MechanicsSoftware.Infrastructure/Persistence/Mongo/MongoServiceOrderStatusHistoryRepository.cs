using MechanicsSoftware.Application.Abstractions;
using MechanicsSoftware.Domain.ValueObjects;
using Microsoft.Extensions.Configuration;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;

namespace MechanicsSoftware.Infrastructure.Persistence.Mongo;

public sealed class MongoServiceOrderStatusHistoryRepository : IServiceOrderStatusHistoryRepository
{
    private readonly IMongoCollection<StatusHistoryDocument> collection;

    public MongoServiceOrderStatusHistoryRepository(IConfiguration configuration)
    {
        var connectionString = configuration["MongoDb:ConnectionString"]
            ?? "mongodb://localhost:27017";
        var databaseName = configuration["MongoDb:Database"] ?? "mechanics_software";
        var database = new MongoClient(connectionString).GetDatabase(databaseName);
        collection = database.GetCollection<StatusHistoryDocument>("service_order_status_history");
        collection.Indexes.CreateOne(new CreateIndexModel<StatusHistoryDocument>(
            Builders<StatusHistoryDocument>.IndexKeys
                .Ascending(document => document.ServiceOrderId)
                .Ascending(document => document.EnteredAt)));
    }

    public Task AppendAsync(
        Guid serviceOrderId,
        ServiceOrderStatus.Status status,
        DateTime enteredAt,
        CancellationToken cancellationToken = default) =>
        collection.InsertOneAsync(
            new StatusHistoryDocument
            {
                Id = ObjectId.GenerateNewId(),
                ServiceOrderId = serviceOrderId,
                Status = status.ToString(),
                EnteredAt = enteredAt
            },
            cancellationToken: cancellationToken);

    public async Task<IReadOnlyList<ServiceOrderStatusHistoryEntry>> GetAllAsync(
        CancellationToken cancellationToken = default)
    {
        var documents = await collection.Find(FilterDefinition<StatusHistoryDocument>.Empty)
            .SortBy(document => document.EnteredAt)
            .ToListAsync(cancellationToken);

        return documents
            .Select(document => new ServiceOrderStatusHistoryEntry(
                document.ServiceOrderId,
                Enum.Parse<ServiceOrderStatus.Status>(document.Status),
                document.EnteredAt))
            .ToList();
    }

    private sealed class StatusHistoryDocument
    {
        [BsonId]
        public ObjectId Id { get; set; }

        public Guid ServiceOrderId { get; set; }
        public string Status { get; set; } = null!;
        public DateTime EnteredAt { get; set; }
    }
}