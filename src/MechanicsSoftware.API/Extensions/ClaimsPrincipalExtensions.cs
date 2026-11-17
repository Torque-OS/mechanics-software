using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using MechanicsSoftware.Domain.Authorization;

namespace MechanicsSoftware.API.Extensions;

public static class ClaimsPrincipalExtensions
{
    /// <summary>
    /// The customer whose orders the caller may see, or <c>null</c> when the caller is staff and may see any order.
    /// Customer tokens are issued by the CPF Lambda (mechanics-lambda) with the customer id as <c>sub</c>.
    /// A customer token without a valid <c>sub</c> yields <see cref="Guid.Empty"/>, which matches no order,
    /// so a malformed token is denied instead of being treated as staff.
    /// </summary>
    public static Guid? OrderOwnerScope(this ClaimsPrincipal user)
    {
        if (Policies.StaffRoles.Any(user.IsInRole))
            return null;

        return Guid.TryParse(user.FindFirstValue(JwtRegisteredClaimNames.Sub), out var customerId)
            ? customerId
            : Guid.Empty;
    }
}
