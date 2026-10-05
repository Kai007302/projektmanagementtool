using Microsoft.Extensions.Configuration;
using ProjectHub.Api.Modules.Privacy;

namespace ProjectHub.Api.UnitTests;

public class PrivacyOptionsTests
{
    [Fact]
    public void Retention_has_defaults_and_zero_keeps_data()
    {
        Assert.Equal(new RetentionOptions(1095, 1095, 1095), RetentionOptions.FromConfiguration(Configuration(new())));
        Assert.Equal(
            new RetentionOptions(30, 0, 3650),
            RetentionOptions.FromConfiguration(Configuration(new()
            {
                [RetentionOptions.NotificationDaysKey] = "30",
                [RetentionOptions.ActivityLogDaysKey] = "0",
                [RetentionOptions.AuditLogDaysKey] = "3650",
            })));
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("ein Jahr")]
    public void Invalid_retention_fails_fast(string value) =>
        Assert.Throws<InvalidOperationException>(() =>
            RetentionOptions.FromConfiguration(Configuration(new() { [RetentionOptions.AuditLogDaysKey] = value })));

    [Theory]
    [InlineData("/rechtliches/datenschutz.html")]
    [InlineData("https://example.com/datenschutz")]
    public void Legal_links_accept_https_and_local_paths(string value) =>
        Assert.Equal(value, LegalInformation.FromConfiguration(Configuration(new() { [LegalInformation.PrivacyNoticeUrlKey] = value })).PrivacyNoticeUrl);

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("http://example.com/datenschutz")]
    [InlineData("//example.com/datenschutz")]
    [InlineData("https://user:secret@example.com/")]
    [InlineData("datenschutz.html")]
    public void Legal_links_reject_everything_else(string value) =>
        Assert.Throws<InvalidOperationException>(() =>
            LegalInformation.FromConfiguration(Configuration(new() { [LegalInformation.ImprintUrlKey] = value })));

    private static IConfiguration Configuration(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();
}
