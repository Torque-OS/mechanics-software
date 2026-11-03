using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using FluentAssertions;
using MechanicsSoftware.Domain.Authorization;
using MechanicsSoftware.Domain.Entities;
using MechanicsSoftware.Infrastructure.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace MechanicsSoftware.UnitTests.Infrastructure;

public class JwtSettingsTests
{
    private const string Secret = "super-secret-key-at-least-32-chars!!";
    private const string CustomerId = "a3f1c2d4-0000-4000-8000-000000000001";

    private static IConfiguration BuildConfig(params (string Key, string? Value)[] overrides)
    {
        var dict = new Dictionary<string, string?> { ["JWT_SECRET"] = Secret };

        foreach (var (key, value) in overrides) dict[key] = value;

        return new ConfigurationBuilder().AddInMemoryCollection(dict).Build();
    }

    private static string LambdaToken(
        string secret = Secret,
        string issuer = JwtSettings.DefaultIssuer,
        string audience = JwtSettings.DefaultAudience,
        string role = Policies.CustomerRole,
        int lifetimeMinutes = 60)
    {
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, CustomerId),
            new Claim("cpf", "52998224725"),
            new Claim(JwtSettings.RoleClaimType, role),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
        };

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));

        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(lifetimeMinutes),
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static ClaimsPrincipal Validate(string token, JwtSettings settings)
    {
        var handler = new JwtSecurityTokenHandler { MapInboundClaims = false };

        return handler.ValidateToken(token, settings.ToValidationParameters(), out _);
    }

    [Fact]
    public void FromConfiguration_MissingIssuerAndAudience_FallsBackToPlatformDefaults()
    {
        var settings = JwtSettings.FromConfiguration(BuildConfig());

        settings.Issuer.Should().Be(JwtSettings.DefaultIssuer);
        settings.Audience.Should().Be(JwtSettings.DefaultAudience);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void FromConfiguration_BlankIssuer_FallsBackToDefault(string configured)
    {
        var settings = JwtSettings.FromConfiguration(BuildConfig(("JWT_ISSUER", configured)));

        settings.Issuer.Should().Be(JwtSettings.DefaultIssuer);
    }

    [Fact]
    public void FromConfiguration_ExplicitValues_AreHonoured()
    {
        var settings = JwtSettings.FromConfiguration(
            BuildConfig(("JWT_ISSUER", "other-issuer"), ("JWT_AUDIENCE", "other-audience")));

        settings.Issuer.Should().Be("other-issuer");
        settings.Audience.Should().Be("other-audience");
    }

    [Fact]
    public void FromConfiguration_MissingSecret_Throws()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection([]).Build();

        var act = () => JwtSettings.FromConfiguration(config);

        act.Should().Throw<InvalidOperationException>().WithMessage("*JWT secret*");
    }

    [Fact]
    public void Validation_AcceptsATokenIssuedByTheLambda()
    {
        var settings = JwtSettings.FromConfiguration(BuildConfig());

        var principal = Validate(LambdaToken(), settings);

        principal.Identity!.Name.Should().Be(CustomerId);
        principal.IsInRole(Policies.CustomerRole).Should().BeTrue();
        principal.FindFirst("cpf")!.Value.Should().Be("52998224725");
    }

    [Fact]
    public void Validation_AcceptsATokenIssuedByTheApplication()
    {
        var config = BuildConfig();
        var settings = JwtSettings.FromConfiguration(config);
        var user = User.Create(
            Guid.NewGuid(), "Staff", "staff@example.com", "hash", User.Roles.Attendant);

        var principal = Validate(new JwtProvider(config).Generate(user).Token, settings);

        principal.Identity!.Name.Should().Be(user.Id.ToString());
        principal.IsInRole(User.Roles.Attendant).Should().BeTrue();
    }

    [Fact]
    public void Validation_RejectsATokenSignedWithAnotherSecret()
    {
        var settings = JwtSettings.FromConfiguration(BuildConfig());
        var token = LambdaToken(secret: "a-completely-different-secret-32ch!!");

        var act = () => Validate(token, settings);

        act.Should().Throw<SecurityTokenSignatureKeyNotFoundException>();
    }

    [Fact]
    public void Validation_RejectsAnUnknownIssuer()
    {
        var settings = JwtSettings.FromConfiguration(BuildConfig());

        var act = () => Validate(LambdaToken(issuer: "someone-else"), settings);

        act.Should().Throw<SecurityTokenInvalidIssuerException>();
    }

    [Fact]
    public void Validation_RejectsAnotherAudience()
    {
        var settings = JwtSettings.FromConfiguration(BuildConfig());

        var act = () => Validate(LambdaToken(audience: "another-api"), settings);

        act.Should().Throw<SecurityTokenInvalidAudienceException>();
    }

    [Fact]
    public void Validation_RejectsAnExpiredToken()
    {
        var settings = JwtSettings.FromConfiguration(BuildConfig());

        var act = () => Validate(LambdaToken(lifetimeMinutes: -5), settings);

        act.Should().Throw<SecurityTokenExpiredException>();
    }
}
