using MechanicsSoftware.Domain.Authorization;
using Microsoft.Extensions.DependencyInjection;

namespace MechanicsSoftware.Infrastructure.Security;

public static class AuthorizationSetup
{
    public static IServiceCollection AddPlatformAuthorization(this IServiceCollection services) =>
        services.AddAuthorization(options =>
        {
            options.AddPolicy(Policies.Staff, policy => policy.RequireRole(Policies.StaffRoles));
            options.AddPolicy(Policies.CustomerOrStaff, policy => policy.RequireRole(Policies.AllRoles));
        });
}
