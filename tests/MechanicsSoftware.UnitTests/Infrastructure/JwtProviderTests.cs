using System.IdentityModel.Tokens.Jwt;
using FluentAssertions;
using MechanicsSoftware.Infrastructure.Security;
using Microsoft.Extensions.Configuration;
using MechanicsSoftware.Domain.Entities;
using MechanicsSoftware.Domain.ValueObjects;
using MechanicsSoftware.Domain.Enums;
using MechanicsSoftware.Domain.Exceptions;

namespace MechanicsSoftware.UnitTests.Infrastructure;

public class JwtProviderTests
{
    private static IConfiguration BuildConfig(string? secret = "super-secret-key-at-least-32-chars!!", string? expirationMinutes = "60")
    {
        var dict = new Dictionary<string, string?>();
        if (secret is not null) dict["JWT_SECRET"] = secret;
        if (expirationMinutes is not null) dict["JWT_EXPIRATION_MINUTES"] = expirationMinutes;
        return new ConfigurationBuilder().AddInMemoryCollection(dict).Build();
    }

    private static User BuildUser() =>
        User.Create(Guid.NewGuid(), "Test User", "test@example.com", "hashed_pwd", User.Roles.Mechanic);

    [Fact]
    public void Generate_ValidUser_ReturnsNonEmptyToken()
    {
        var provider = new JwtProvider(BuildConfig());

        var result = provider.Generate(BuildUser());

        result.Token.Should().NotBeNullOrWhiteSpace();
        result.ExpiresAt.Should().BeAfter(DateTime.UtcNow);
    }

    [Fact]
    public void Generate_TokenContainsUserClaims()
    {
        var provider = new JwtProvider(BuildConfig());
        var user = BuildUser();

        var result = provider.Generate(user);

        var handler = new JwtSecurityTokenHandler();
        var parsed = handler.ReadJwtToken(result.Token);

        parsed.Subject.Should().Be(user.Id.ToString());
        parsed.Claims.Should().Contain(c => c.Value == user.Email.Value);
        parsed.Claims.Should().Contain(c => c.Value == user.Role);
    }

    [Fact]
    public void Generate_ExpiresAtMatchesConfig()
    {
        var provider = new JwtProvider(BuildConfig(expirationMinutes: "30"));

        var before = DateTime.UtcNow.AddMinutes(29);
        var result = provider.Generate(BuildUser());
        var after = DateTime.UtcNow.AddMinutes(31);

        result.ExpiresAt.Should().BeAfter(before).And.BeBefore(after);
    }

    [Fact]
    public void Generate_InvalidExpirationMinutes_DefaultsTo60()
    {
        var provider = new JwtProvider(BuildConfig(expirationMinutes: "not-a-number"));

        var before = DateTime.UtcNow.AddMinutes(59);
        var result = provider.Generate(BuildUser());
        var after = DateTime.UtcNow.AddMinutes(61);

        result.ExpiresAt.Should().BeAfter(before).And.BeBefore(after);
    }

    [Fact]
    public void Generate_NonPositiveExpirationMinutes_DefaultsTo60()
    {
        var provider = new JwtProvider(BuildConfig(expirationMinutes: "0"));

        var before = DateTime.UtcNow.AddMinutes(59);
        var result = provider.Generate(BuildUser());
        var after = DateTime.UtcNow.AddMinutes(61);

        result.ExpiresAt.Should().BeAfter(before).And.BeBefore(after);
    }

    [Fact]
    public void Constructor_MissingSecret_ThrowsInvalidOperationException()
    {
        var config = BuildConfig(secret: null);

        var act = () => new JwtProvider(config);

        act.Should().Throw<InvalidOperationException>().WithMessage("*JWT secret*");
    }
}
