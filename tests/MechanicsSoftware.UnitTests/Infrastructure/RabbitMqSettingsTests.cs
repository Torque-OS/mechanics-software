using FluentAssertions;
using MechanicsSoftware.Infrastructure.Messaging;
using Microsoft.Extensions.Configuration;

namespace MechanicsSoftware.UnitTests.Infrastructure;

public class RabbitMqSettingsTests
{
    private const string Password = "s3cr3t-broker-password";

    private static IConfiguration BuildConfig(params (string Key, string? Value)[] overrides)
    {
        var dict = new Dictionary<string, string?>
        {
            ["RABBITMQ_HOST"] = "rabbitmq.local",
            ["RABBITMQ_USERNAME"] = "mechanics",
            ["RABBITMQ_PASSWORD"] = Password,
        };

        foreach (var (key, value) in overrides) dict[key] = value;

        return new ConfigurationBuilder().AddInMemoryCollection(dict).Build();
    }

    [Fact]
    public void FromConfiguration_OnlyRequiredValues_AppliesDefaultPortAndVirtualHost()
    {
        var settings = RabbitMqSettings.FromConfiguration(BuildConfig());

        settings.Host.Should().Be("rabbitmq.local");
        settings.Port.Should().Be(5672);
        settings.VirtualHost.Should().Be("/");
        settings.Username.Should().Be("mechanics");
        settings.Password.Should().Be(Password);
    }

    [Fact]
    public void FromConfiguration_AllValues_UsesConfiguredPortAndVirtualHost()
    {
        var settings = RabbitMqSettings.FromConfiguration(BuildConfig(
            ("RABBITMQ_PORT", "5673"),
            ("RABBITMQ_VHOST", "os-service")));

        settings.Port.Should().Be(5673);
        settings.VirtualHost.Should().Be("os-service");
    }

    [Theory]
    [InlineData("not-a-number")]
    [InlineData("0")]
    [InlineData("70000")]
    public void FromConfiguration_InvalidPort_FallsBackToDefaultPort(string port)
    {
        var settings = RabbitMqSettings.FromConfiguration(BuildConfig(("RABBITMQ_PORT", port)));

        settings.Port.Should().Be(5672);
    }

    [Theory]
    [InlineData("RABBITMQ_HOST")]
    [InlineData("RABBITMQ_USERNAME")]
    [InlineData("RABBITMQ_PASSWORD")]
    public void FromConfiguration_MissingRequiredValue_ThrowsNamingTheVariable(string key)
    {
        var act = () => RabbitMqSettings.FromConfiguration(BuildConfig((key, null)));

        act.Should().Throw<InvalidOperationException>().WithMessage($"*{key}*");
    }

    [Fact]
    public void ToString_DoesNotExposePassword()
    {
        var settings = RabbitMqSettings.FromConfiguration(BuildConfig());

        settings.ToString().Should().NotContain(Password).And.Contain("rabbitmq.local");
    }
}
