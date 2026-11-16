using FluentAssertions;
using MechanicsSoftware.Infrastructure.Billing;
using Microsoft.Extensions.Configuration;

namespace MechanicsSoftware.UnitTests.Infrastructure;

public class BillingSettingsTests
{
    private static IConfiguration Config(string? url) =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["BILLING_API_URL"] = url }).Build();

    [Theory]
    [InlineData("http://billing:8080")]
    [InlineData("http://billing:8080/")]
    public void FromConfiguration_AbsoluteUrl_EndsWithASlashSoRelativePathsAreKept(string url)
    {
        var settings = BillingSettings.FromConfiguration(Config(url));

        settings.BaseAddress.Should().Be(new Uri("http://billing:8080/"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void FromConfiguration_Missing_ThrowsNamingTheVariable(string? url)
    {
        var act = () => BillingSettings.FromConfiguration(Config(url));

        act.Should().Throw<InvalidOperationException>().WithMessage("*BILLING_API_URL*");
    }

    [Fact]
    public void FromConfiguration_RelativeUrl_Throws()
    {
        var act = () => BillingSettings.FromConfiguration(Config("billing:8080/api"));

        act.Should().Throw<InvalidOperationException>().WithMessage("*absolute URL*");
    }
}
