using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MechanicsSoftware.Domain.Entities;
using MechanicsSoftware.Domain.ValueObjects;
using MechanicsSoftware.Domain.Enums;
using MechanicsSoftware.Domain.Exceptions;

namespace MechanicsSoftware.Infrastructure.Persistence.Configurations;

public sealed class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> builder)
    {
        builder.ToTable("customers");

        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).HasColumnName("id");

        builder.Property(c => c.Name)
            .HasColumnName("name")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(c => c.Phone)
            .HasColumnName("phone")
            .HasMaxLength(20)
            .IsRequired();

        // TaxId — PersonType is inferred from digit count (11=CPF/INDIVIDUAL, 14=CNPJ/COMPANY)
        builder.Property(c => c.Document)
            .HasConversion(
                v => v.Value,
                v => new TaxId(v, v.Length == 11 ? PersonType.INDIVIDUAL : PersonType.COMPANY))
            .HasColumnName("document")
            .HasMaxLength(14)
            .IsRequired();

        builder.Property(c => c.Email)
            .HasConversion(
                v => v.Value,
                v => new Email(v))
            .HasColumnName("email")
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(c => c.Active)
            .HasColumnName("active")
            .HasDefaultValue(true)
            .IsRequired();

        builder.HasIndex(c => c.Document)
            .IsUnique()
            .HasDatabaseName("ix_customers_document");

        builder.HasIndex(c => c.Email)
            .IsUnique()
            .HasDatabaseName("ix_customers_email");
    }
}
