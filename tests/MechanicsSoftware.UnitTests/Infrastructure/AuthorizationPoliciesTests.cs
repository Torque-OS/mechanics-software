using System.Security.Claims;
using FluentAssertions;
using MechanicsSoftware.Domain.Authorization;
using MechanicsSoftware.Domain.Entities;
using MechanicsSoftware.Infrastructure.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;

namespace MechanicsSoftware.UnitTests.Infrastructure;

public class AuthorizationPoliciesTests
{
    private readonly IAuthorizationService _authorization = new ServiceCollection()
        .AddLogging()
        .AddPlatformAuthorization()
        .BuildServiceProvider()
        .GetRequiredService<IAuthorizationService>();

    private static ClaimsPrincipal PrincipalWith(string role) =>
        new(new ClaimsIdentity(
            [new Claim(JwtSettings.RoleClaimType, role)],
            authenticationType: "Test",
            nameType: null,
            roleType: JwtSettings.RoleClaimType));

    private static readonly ClaimsPrincipal Anonymous = new(new ClaimsIdentity());

    [Theory]
    [InlineData(User.Roles.Admin)]
    [InlineData(User.Roles.Attendant)]
    [InlineData(User.Roles.Mechanic)]
    public async Task Staff_AllowsEveryShopRole(string role)
    {
        var result = await _authorization.AuthorizeAsync(PrincipalWith(role), null, Policies.Staff);

        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public async Task Staff_RejectsACustomerToken()
    {
        var result = await _authorization.AuthorizeAsync(
            PrincipalWith(Policies.CustomerRole), null, Policies.Staff);

        result.Succeeded.Should().BeFalse();
    }

    [Fact]
    public async Task CustomerOrStaff_AllowsACustomerToken()
    {
        var result = await _authorization.AuthorizeAsync(
            PrincipalWith(Policies.CustomerRole), null, Policies.CustomerOrStaff);

        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public async Task CustomerOrStaff_AllowsStaff()
    {
        var result = await _authorization.AuthorizeAsync(
            PrincipalWith(User.Roles.Admin), null, Policies.CustomerOrStaff);

        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public async Task CustomerOrStaff_RejectsAnUnknownRole()
    {
        var result = await _authorization.AuthorizeAsync(
            PrincipalWith("SOMETHING_ELSE"), null, Policies.CustomerOrStaff);

        result.Succeeded.Should().BeFalse();
    }

    [Fact]
    public async Task CustomerOrStaff_RejectsAnAnonymousCaller()
    {
        var result = await _authorization.AuthorizeAsync(Anonymous, null, Policies.CustomerOrStaff);

        result.Succeeded.Should().BeFalse();
    }

    [Fact]
    public void CustomerRole_IsNotAStaffRole()
    {
        Policies.StaffRoles.Should().NotContain(Policies.CustomerRole);
        Policies.AllRoles.Should().Contain(Policies.CustomerRole);
    }
}
