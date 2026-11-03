using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MechanicsSoftware.Domain.Entities;
using MechanicsSoftware.Domain.ValueObjects;
using MechanicsSoftware.Domain.Enums;
using MechanicsSoftware.Domain.Exceptions;

namespace MechanicsSoftware.Infrastructure.Persistence.Configurations;

public sealed class ServiceOrderConfiguration : IEntityTypeConfiguration<ServiceOrder>
{
    public void Configure(EntityTypeBuilder<ServiceOrder> builder)
    {
        builder.ToTable("service_orders");

        builder.HasKey(so => so.Id);
        builder.Property(so => so.Id).HasColumnName("id");

        builder.Property(so => so.CustomerId)
            .HasColumnName("customer_id")
            .IsRequired();

        builder.Property(so => so.VehicleId)
            .HasColumnName("vehicle_id")
            .IsRequired();

        builder.Property(so => so.Status)
            .HasConversion(
                v => v.ToString(),
                v => ParseOrderStatus(v))
            .HasColumnName("status")
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(so => so.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        builder.Property(so => so.CompletedAt)
            .HasColumnName("completed_at");

        builder.Property(so => so.DeliveredAt)
            .HasColumnName("delivered_at");

        builder.Navigation(so => so.ServiceItems)
            .HasField("_serviceItems")
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.Navigation(so => so.PartItems)
            .HasField("_partItems")
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.OwnsMany(so => so.ServiceItems, si =>
        {
            si.ToTable("service_items");
            si.HasKey(s => s.Id);
            si.Property(s => s.Id).HasColumnName("id");
            si.WithOwner().HasForeignKey(s => s.ServiceOrderId);
            si.Property(s => s.ServiceOrderId).HasColumnName("service_order_id");
            si.HasIndex(s => s.ServiceOrderId).HasDatabaseName("ix_service_items_service_order_id");
            si.Property(s => s.ServiceId).HasColumnName("service_id").IsRequired();
            si.Property(s => s.ServiceName)
                .HasColumnName("service_name")
                .HasMaxLength(100)
                .IsRequired();
            si.Property(s => s.UnitPrice)
                .HasConversion(m => m.Cents, m => new Money(m))
                .HasColumnName("unit_price")
                .IsRequired();
            si.Property(s => s.Quantity).HasColumnName("quantity").IsRequired();
            si.Ignore(s => s.Total);
        });

        builder.OwnsMany(so => so.PartItems, pi =>
        {
            pi.ToTable("part_items");
            pi.HasKey(p => p.Id);
            pi.Property(p => p.Id).HasColumnName("id");
            pi.WithOwner().HasForeignKey(p => p.ServiceOrderId);
            pi.Property(p => p.ServiceOrderId).HasColumnName("service_order_id");
            pi.HasIndex(p => p.ServiceOrderId).HasDatabaseName("ix_part_items_service_order_id");
            pi.Property(p => p.PartId).HasColumnName("part_id").IsRequired();
            pi.Property(p => p.PartName)
                .HasColumnName("part_name")
                .HasMaxLength(100)
                .IsRequired();
            pi.Property(p => p.UnitPrice)
                .HasConversion(m => m.Cents, m => new Money(m))
                .HasColumnName("unit_price")
                .IsRequired();
            pi.Property(p => p.Quantity).HasColumnName("quantity").IsRequired();
            pi.Property(p => p.Availability)
                .HasConversion<string>()
                .HasColumnName("availability")
                .IsRequired();
            pi.Ignore(p => p.Total);
        });

        // Budget is owned by the Billing Service (ADR-010); we keep only a reference.
        builder.Property(so => so.BudgetId)
            .HasColumnName("budget_id");

        builder.Property(so => so.BudgetTotal)
            .HasConversion(m => m == null ? (int?)null : m.Cents, v => v == null ? null : new Money(v.Value))
            .HasColumnName("budget_total");

        builder.HasIndex(so => so.CustomerId).HasDatabaseName("ix_service_orders_customer_id");
        builder.HasIndex(so => so.VehicleId).HasDatabaseName("ix_service_orders_vehicle_id");

        builder.HasOne<Customer>()
            .WithMany()
            .HasForeignKey(so => so.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Vehicle>()
            .WithMany()
            .HasForeignKey(so => so.VehicleId)
            .OnDelete(DeleteBehavior.Restrict);
    }

    private static readonly Dictionary<string, ServiceOrderStatus.Status> _orderStatusMap = new()
    {
        ["RECEIVED"]          = ServiceOrderStatus.Status.Received,
        ["IN_DIAGNOSIS"]      = ServiceOrderStatus.Status.InDiagnosis,
        ["AWAITING_APPROVAL"] = ServiceOrderStatus.Status.AwaitingApproval,
        ["AWAITING_PAYMENT"]  = ServiceOrderStatus.Status.AwaitingPayment,
        ["IN_EXECUTION"]      = ServiceOrderStatus.Status.InExecution,
        ["COMPLETED"]         = ServiceOrderStatus.Status.Completed,
        ["DELIVERED"]         = ServiceOrderStatus.Status.Delivered,
        ["CANCELLED"]         = ServiceOrderStatus.Status.Cancelled,
    };

    internal static ServiceOrderStatus ParseOrderStatus(string value) =>
        _orderStatusMap.TryGetValue(value, out var status)
            ? new ServiceOrderStatus(status)
            : throw new InvalidOperationException($"Unknown service order status: '{value}'");
}
