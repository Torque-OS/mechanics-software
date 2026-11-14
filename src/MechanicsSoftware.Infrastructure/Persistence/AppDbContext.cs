using System.Reflection;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using MechanicsSoftware.Application.Abstractions;
using MechanicsSoftware.Domain.Entities;
using MechanicsSoftware.Domain.ValueObjects;
using MechanicsSoftware.Domain.Enums;
using MechanicsSoftware.Domain.Exceptions;

namespace MechanicsSoftware.Infrastructure.Persistence;

public sealed class AppDbContext : DbContext, IAppDbContext
{
    private readonly IServiceOrderStatusHistoryStore? _historyStore;
    private readonly ILogger<AppDbContext>? _logger;

    public AppDbContext(
        DbContextOptions<AppDbContext> options,
        IServiceOrderStatusHistoryStore? historyStore = null,
        ILogger<AppDbContext>? logger = null) : base(options)
    {
        _historyStore = historyStore;
        _logger = logger;
    }

    public DbSet<User> Users { get; set; } = null!;
    public DbSet<Customer> Customers { get; set; } = null!;
    public DbSet<Vehicle> Vehicles { get; set; } = null!;
    public DbSet<Part> Parts { get; set; } = null!;
    public DbSet<Service> Services { get; set; } = null!;
    public DbSet<ServiceOrder> ServiceOrders { get; set; } = null!;
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());

        // MassTransit transactional outbox (ADR-011). Table names follow the snake_case
        // convention of this database; column names are owned by MassTransit and kept as-is.
        modelBuilder.AddInboxStateEntity(entity => entity.ToTable("inbox_state"));
        modelBuilder.AddOutboxMessageEntity(entity => entity.ToTable("outbox_message"));
        modelBuilder.AddOutboxStateEntity(entity => entity.ToTable("outbox_state"));
    }

    // Workaround for EF Core 9.0.14 + Npgsql 9.0.4: brand-new owned entities (collection items
    // and owned scalars) are sometimes attached as Modified instead of Added even when the
    // owner is loaded with .Include(...) of the navigation. The resulting UPDATE affects 0
    // rows and throws DbUpdateConcurrencyException. We promote those entries before saving.
    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        ChangeTracker.DetectChanges();
        var statusHistory = CollectServiceOrderStatusChanges();
        PromoteNewOwnedCollectionItems();
        await PromoteNewOwnedScalarsAsync(cancellationToken);

        var auto = ChangeTracker.AutoDetectChangesEnabled;
        ChangeTracker.AutoDetectChangesEnabled = false;
        try
        {
            var result = await base.SaveChangesAsync(cancellationToken);
            if (_historyStore is not null && statusHistory.Count > 0)
            {
                try
                {
                    await _historyStore.AppendAsync(statusHistory, cancellationToken);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    _logger?.LogError(
                        exception,
                        "Failed to persist service order status history after saving the order.");
                }
            }

            return result;
        }
        finally
        {
            ChangeTracker.AutoDetectChangesEnabled = auto;
        }
    }

    private List<ServiceOrderStatusHistoryRecord> CollectServiceOrderStatusChanges()
    {
        var records = new List<ServiceOrderStatusHistoryRecord>();
        foreach (var entry in ChangeTracker.Entries<ServiceOrder>().ToList())
        {
            if (entry.State == EntityState.Added)
            {
                records.Add(CreateHistoryRecord(
                    entry.Entity,
                    entry.Entity.Status.Value,
                    entry.Entity.CreatedAt));
                continue;
            }

            if (entry.State != EntityState.Modified)
                continue;

            var originalStatus = entry.Property(order => order.Status).OriginalValue;
            if (originalStatus.Value != entry.Entity.Status.Value)
                records.Add(CreateHistoryRecord(
                    entry.Entity,
                    entry.Entity.Status.Value,
                    DateTime.UtcNow));
        }

        return records;
    }

    private static ServiceOrderStatusHistoryRecord CreateHistoryRecord(
        ServiceOrder order,
        ServiceOrderStatus.Status status,
        DateTime enteredAt) =>
        new(Guid.NewGuid(), order.Id, new ServiceOrderStatus(status).ToString(), enteredAt);

    // Owned-collection items in this domain are append-only (ServiceItems, PartItems,
    // StockMovements). Any Modified entry is therefore a freshly-added item. No DB round-trip.
    private void PromoteNewOwnedCollectionItems()
    {
        foreach (var entry in ChangeTracker.Entries())
        {
            if (entry.State != EntityState.Modified) continue;
            var ownership = entry.Metadata.FindOwnership();
            if (ownership is null || ownership.IsUnique) continue;
            entry.State = EntityState.Added;
        }
    }

    // Unique owned scalars ARE mutated after creation, so we cannot blindly flip
    // Modified → Added. A single DB lookup distinguishes new from existing. No entity currently
    // uses a mutable unique owned scalar (Budget moved to the Billing Service — ADR-010), but
    // this stays in place for the next one.
    private async Task PromoteNewOwnedScalarsAsync(CancellationToken cancellationToken)
    {
        var candidates = ChangeTracker.Entries()
            .Where(e => e.State == EntityState.Modified
                     && e.Metadata.FindOwnership() is { IsUnique: true })
            .ToList();

        foreach (var entry in candidates)
            await PromoteIfMissingAsync(entry, cancellationToken);
    }

    private static async Task PromoteIfMissingAsync(EntityEntry entry, CancellationToken ct)
    {
        if (await entry.GetDatabaseValuesAsync(ct) is null)
            entry.State = EntityState.Added;
    }
}
