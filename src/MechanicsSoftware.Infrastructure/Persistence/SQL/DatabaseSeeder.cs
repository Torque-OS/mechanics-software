using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;
using MechanicsSoftware.Application.Abstractions;
using MechanicsSoftware.Domain.Entities;
using MechanicsSoftware.Domain.ValueObjects;
using MechanicsSoftware.Domain.Enums;
using MechanicsSoftware.Domain.Exceptions;

namespace MechanicsSoftware.Infrastructure.Persistence.SQL;

[ExcludeFromCodeCoverage]
public sealed class DatabaseSeeder(AppDbContext db, IPasswordHasher hasher)
{
    private const string DefaultAdminEmail    = "admin@mechanics.local";
    private const string DefaultAdminName     = "Admin";
    private const string DefaultAdminPassword = "Admin@123";

    // Fixed IDs — stable across container restarts; used by the Swagger demo script.
    internal static readonly Guid Customer1Id = new("a1000000-0000-0000-0000-000000000001");
    internal static readonly Guid Customer2Id = new("a2000000-0000-0000-0000-000000000002");
    internal static readonly Guid Customer3Id = new("a3000000-0000-0000-0000-000000000003");

    internal static readonly Guid Vehicle1Id  = new("b1000000-0000-0000-0000-000000000001");
    internal static readonly Guid Vehicle2Id  = new("b2000000-0000-0000-0000-000000000002");
    internal static readonly Guid Vehicle3Id  = new("b3000000-0000-0000-0000-000000000003");

    internal static readonly Guid Service1Id  = new("c1000000-0000-0000-0000-000000000001");
    internal static readonly Guid Service2Id  = new("c2000000-0000-0000-0000-000000000002");
    internal static readonly Guid Service3Id  = new("c3000000-0000-0000-0000-000000000003");

    internal static readonly Guid Part1Id     = new("d1000000-0000-0000-0000-000000000001");
    internal static readonly Guid Part2Id     = new("d2000000-0000-0000-0000-000000000002");
    internal static readonly Guid Part3Id     = new("d3000000-0000-0000-0000-000000000003");

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        await SeedAdminAsync(cancellationToken);
        await CleanDomainDataAsync(cancellationToken);
        await SeedServicesAsync(cancellationToken);
        await SeedPartsAsync(cancellationToken);
        await SeedCustomersAndVehiclesAsync(cancellationToken);
    }

    // Wipe all domain data on every startup so seed records never conflict with
    // leftovers from previous runs (unique constraints on name, code, plate, etc.).
    // Users are intentionally kept so the admin password survives restarts.
    private async Task CleanDomainDataAsync(CancellationToken ct)
    {
        await db.Database.ExecuteSqlRawAsync("""
            TRUNCATE TABLE
                stock_movements,
                part_items,
                service_items,
                service_orders,
                vehicles,
                customers,
                services,
                parts
            CASCADE
            """, ct);
    }

    private async Task SeedAdminAsync(CancellationToken ct)
    {
        var adminEmail    = Environment.GetEnvironmentVariable("SEED_ADMIN_EMAIL")    ?? DefaultAdminEmail;
        var adminPassword = Environment.GetEnvironmentVariable("SEED_ADMIN_PASSWORD") ?? DefaultAdminPassword;

        if (await db.Users.AnyAsync(u => u.Email == new Email(adminEmail), ct))
            return;

        db.Users.Add(User.Create(
            id:           Guid.NewGuid(),
            name:         DefaultAdminName,
            email:        adminEmail,
            passwordHash: hasher.Hash(adminPassword),
            role:         User.Roles.Admin));

        await db.SaveChangesAsync(ct);
    }

    private async Task SeedServicesAsync(CancellationToken ct)
    {
        db.Services.AddRange(
            Service.Create(Service1Id, "Troca de Óleo",
                "Troca de óleo do motor + filtro",
                new Money(9_000), 60),
            Service.Create(Service2Id, "Alinhamento e Balanceamento",
                "Alinhamento de direção + balanceamento de rodas",
                new Money(15_000), 90),
            Service.Create(Service3Id, "Revisão dos Freios",
                "Inspeção e substituição das pastilhas de freio",
                new Money(25_000), 120));

        await db.SaveChangesAsync(ct);
    }

    private async Task SeedPartsAsync(CancellationToken ct)
    {
        db.Parts.AddRange(
            Part.Create(Part1Id, "OL-5W30",      "Óleo Motor 5W30",
                "Óleo de motor sintético 5W30 — 1 L",        new Money(4_500), 100),
            Part.Create(Part2Id, "FILT-AR-001",  "Filtro de Ar",
                "Filtro de ar do motor universal",            new Money(3_500),  80),
            Part.Create(Part3Id, "PAST-FREIO",   "Pastilha de Freio Dianteira",
                "Jogo de pastilhas para eixo dianteiro",      new Money(8_000),  60));

        await db.SaveChangesAsync(ct);
    }

    private async Task SeedCustomersAndVehiclesAsync(CancellationToken ct)
    {
        db.Customers.AddRange(
            Customer.Create(Customer1Id, "Carlos Silva",    "52998224725", PersonType.INDIVIDUAL, "carlos@silva.com",    "11987654321"),
            Customer.Create(Customer2Id, "Ana Souza",       "98765432100", PersonType.INDIVIDUAL, "ana@souza.com",       "21976543210"),
            Customer.Create(Customer3Id, "Roberto Mendes",  "11144477735", PersonType.INDIVIDUAL, "roberto@mendes.com",  "31965432109"));

        await db.SaveChangesAsync(ct);

        db.Vehicles.AddRange(
            Vehicle.Create(Vehicle1Id, new LicensePlate("ABC1234"), "Toyota",  "Corolla", 2022, Customer1Id),
            Vehicle.Create(Vehicle2Id, new LicensePlate("DEF5678"), "Honda",   "Civic",   2021, Customer2Id),
            Vehicle.Create(Vehicle3Id, new LicensePlate("GHI9012"), "Renault", "Sandero", 2020, Customer3Id));

        await db.SaveChangesAsync(ct);
    }
}
