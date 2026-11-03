using MechanicsSoftware.Domain.Entities;

namespace MechanicsSoftware.Domain.Authorization;

public static class Policies
{
    public const string Staff = nameof(Staff);

    public const string CustomerOrStaff = nameof(CustomerOrStaff);

    public const string CustomerRole = "CUSTOMER";

    public static readonly string[] StaffRoles =
    [
        User.Roles.Admin,
        User.Roles.Attendant,
        User.Roles.Mechanic,
    ];

    public static readonly string[] AllRoles = [.. StaffRoles, CustomerRole];
}
