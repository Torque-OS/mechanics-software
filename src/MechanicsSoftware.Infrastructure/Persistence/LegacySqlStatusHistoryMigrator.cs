using MechanicsSoftware.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace MechanicsSoftware.Infrastructure.Persistence;

public sealed class LegacySqlStatusHistoryMigrator(
    AppDbContext db,
    IServiceOrderStatusHistoryStore historyStore)
{
    private const string LegacyTableName = "service_order_status_history_legacy";

    public async Task MigrateAsync(CancellationToken cancellationToken = default)
    {
        if (!await TableExistsAsync(cancellationToken))
            return;

        var records = await ReadRecordsAsync(cancellationToken);
        if (records.Count > 0)
            await historyStore.AppendAsync(records, cancellationToken);

        await db.Database.ExecuteSqlRawAsync(
            $"DROP TABLE \"{LegacyTableName}\"",
            cancellationToken);
    }

    private async Task<bool> TableExistsAsync(CancellationToken cancellationToken)
    {
        var connection = db.Database.GetDbConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT EXISTS (
                SELECT 1
                FROM information_schema.tables
                WHERE table_schema = 'public' AND table_name = 'service_order_status_history_legacy'
            )
            """;

        if (connection.State != System.Data.ConnectionState.Open)
            await connection.OpenAsync(cancellationToken);

        return (bool)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    private async Task<List<ServiceOrderStatusHistoryRecord>> ReadRecordsAsync(
        CancellationToken cancellationToken)
    {
        var records = new List<ServiceOrderStatusHistoryRecord>();
        var connection = db.Database.GetDbConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, service_order_id, status, entered_at
            FROM service_order_status_history_legacy
            ORDER BY service_order_id, entered_at
            """;

        if (connection.State != System.Data.ConnectionState.Open)
            await connection.OpenAsync(cancellationToken);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            records.Add(new ServiceOrderStatusHistoryRecord(
                reader.GetGuid(0),
                reader.GetGuid(1),
                reader.GetString(2),
                reader.GetDateTime(3)));
        }

        return records;
    }
}
