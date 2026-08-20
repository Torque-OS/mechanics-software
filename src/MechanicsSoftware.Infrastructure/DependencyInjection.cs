using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MechanicsSoftware.Application.Abstractions;
using MechanicsSoftware.Infrastructure.Notifications;
using MechanicsSoftware.Infrastructure.Persistence;
using MechanicsSoftware.Infrastructure.Persistence.SQL;
using MechanicsSoftware.Infrastructure.Security;

namespace MechanicsSoftware.Infrastructure;

[ExcludeFromCodeCoverage]
public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? configuration["DATABASE_URL"]
            ?? throw new InvalidOperationException(
                "Connection string not found. Set 'ConnectionStrings:DefaultConnection' or 'DATABASE_URL'.");

        var jwt = JwtSettings.FromConfiguration(configuration);

        services.AddDbContext<AppDbContext>(options =>
            options.UseNpgsql(connectionString));

        services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());
        services.AddSingleton<IPasswordHasher, BCryptPasswordHasher>();
        services.AddScoped<IJwtProvider, JwtProvider>();
        services.AddScoped<DatabaseSeeder>();
        services.AddSingleton<IEmailNotifier, SmtpEmailNotifier>();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.MapInboundClaims = false;
                options.TokenValidationParameters = jwt.ToValidationParameters();
            });

        services.AddPlatformAuthorization();

        return services;
    }
}
