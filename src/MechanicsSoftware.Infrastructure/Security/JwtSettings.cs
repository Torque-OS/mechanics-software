using System.IdentityModel.Tokens.Jwt;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace MechanicsSoftware.Infrastructure.Security;

public sealed record JwtSettings(string Secret, string Issuer, string Audience, int ExpirationMinutes)
{
    public const string DefaultIssuer = "torque-os";
    public const string DefaultAudience = "mechanics-software-api";

    public const string RoleClaimType = "role";

    private const int DefaultExpirationMinutes = 60;

    public static JwtSettings FromConfiguration(IConfiguration configuration)
    {
        var secret = configuration["JWT_SECRET"]
            ?? throw new InvalidOperationException(
                "JWT secret not configured. Set the 'JWT_SECRET' environment variable.");

        var expirationMinutes =
            int.TryParse(configuration["JWT_EXPIRATION_MINUTES"], out var minutes) && minutes > 0
                ? minutes
                : DefaultExpirationMinutes;

        return new JwtSettings(
            secret,
            Fallback(configuration["JWT_ISSUER"], DefaultIssuer),
            Fallback(configuration["JWT_AUDIENCE"], DefaultAudience),
            expirationMinutes);
    }

    public SymmetricSecurityKey SigningKey => new(Encoding.UTF8.GetBytes(Secret));

    public TokenValidationParameters ToValidationParameters() => new()
    {
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = SigningKey,
        ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
        ValidateIssuer = true,
        ValidIssuer = Issuer,
        ValidateAudience = true,
        ValidAudience = Audience,
        ValidateLifetime = true,
        ClockSkew = TimeSpan.Zero,
        NameClaimType = JwtRegisteredClaimNames.Sub,
        RoleClaimType = RoleClaimType,
    };

    private static string Fallback(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value;
}
