using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using MechanicsSoftware.Application.Abstractions;
using MechanicsSoftware.Domain.Entities;
using MechanicsSoftware.Domain.ValueObjects;
using MechanicsSoftware.Domain.Enums;
using MechanicsSoftware.Domain.Exceptions;

namespace MechanicsSoftware.Infrastructure.Persistence;

public sealed class AppDbContext : DbContext, IAppDbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<User> Users { get; set; } = null!;
    public DbSet<Customer> Customers { get; set; } = null!;
    public DbSet<Vehicle> Vehicles { get; set; } = null!;
    public DbSet<Part> Parts { get; set; } = null!;
    public DbSet<Service> Services { get; set; } = null!;
    public DbSet<ServiceOrder> ServiceOrders { get; set; } = null!;
    public DbSet<ServiceOrderStatusHistory> ServiceOrderStatusHistory { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());
    }

    // Workaround for EF Core 9.0.14 + Npgsql 9.0.4: brand-new owned entities (collection items
    // and owned scalars) are sometimes attached as Modified instead of Added even when the
    // owner is loaded with .Include(...) of the navigation. The resulting UPDATE affects 0
    // rows and throws DbUpdateConcurrencyException. We promote those entries before saving.
    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        ChangeTracker.DetectChanges();
        RecordServiceOrderStatusChanges();
        PromoteNewOwnedCollectionItems();
        await PromoteNewOwnedScalarsAsync(cancellationToken);

        var auto = ChangeTracker.AutoDetectChangesEnabled;
        ChangeTracker.AutoDetectChangesEnabled = false;
        try
        {
            return await base.SaveChangesAsync(cancellationToken);
        }
        finally
        {
            ChangeTracker.AutoDetectChangesEnabled = auto;
        }
    }

    private void RecordServiceOrderStatusChanges()
    {
        foreach (var entry in ChangeTracker.Entries<ServiceOrder>().ToList())
        {
            if (entry.State == EntityState.Added)
            {
                AddStatusHistory(entry.Entity, entry.Entity.Status.Value, entry.Entity.CreatedAt);
                continue;
            }

            if (entry.State != EntityState.Modified)
                continue;

            var originalStatus = entry.Property(order => order.Status).OriginalValue;
            if (originalStatus.Value != entry.Entity.Status.Value)
                AddStatusHistory(entry.Entity, entry.Entity.Status.Value, DateTime.UtcNow);
        }
    }

    private void AddStatusHistory(
        ServiceOrder order,
        ServiceOrderStatus.Status status,
        DateTime enteredAt) =>
        Set<ServiceOrderStatusHistory>().Add(
            MechanicsSoftware.Domain.Entities.ServiceOrderStatusHistory.Create(
                order.Id,
                status,
                enteredAt));

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

    // Owned scalars (e.g. Budget) ARE mutated after creation (Approve/Reject), so we cannot
    // blindly flip Modified → Added. A single DB lookup distinguishes new from existing.
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
