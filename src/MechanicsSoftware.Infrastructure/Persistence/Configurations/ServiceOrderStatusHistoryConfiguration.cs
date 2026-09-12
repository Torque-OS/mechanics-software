using MechanicsSoftware.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MechanicsSoftware.Infrastructure.Persistence.Configurations;

public sealed class ServiceOrderStatusHistoryConfiguration
    : IEntityTypeConfiguration<ServiceOrderStatusHistory>
{
    public void Configure(EntityTypeBuilder<ServiceOrderStatusHistory> builder)
    {
        builder.ToTable("service_order_status_history");
        builder.HasKey(history => history.Id);

        builder.Property(history => history.Id).HasColumnName("id");
        builder.Property(history => history.ServiceOrderId)
            .HasColumnName("service_order_id")
            .IsRequired();
        builder.Property(history => history.Status)
            .HasConversion<string>()
            .HasColumnName("status")
            .HasMaxLength(30)
            .IsRequired();
        builder.Property(history => history.EnteredAt)
            .HasColumnName("entered_at")
            .IsRequired();
        builder.HasOne<ServiceOrder>()
            .WithMany()
            .HasForeignKey(history => history.ServiceOrderId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(history => new { history.ServiceOrderId, history.EnteredAt });
    }
}