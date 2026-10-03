using Microsoft.Extensions.Configuration;
using ProjectHub.Api.Infrastructure.Http;

namespace ProjectHub.Api.UnitTests;

public sealed class HttpHardeningOptionsTests
{
    private static HttpHardeningOptions Read(params (string Key, string Value)[] values) =>
        HttpHardeningOptions.FromConfiguration(new ConfigurationBuilder()
            .AddInMemoryCollection(values.Select(v => new KeyValuePair<string, string?>(v.Key, v.Value)))
            .Build());

    [Fact]
    public void Defaults_are_safe_without_configuration() =>
        Assert.Equal(new HttpHardeningOptions(false, 4 * 1024 * 1024, 600, 30, 120), Read());

    [Fact]
    public void Values_come_from_configuration_and_zero_switches_a_limit_off() =>
        Assert.Equal(
            new HttpHardeningOptions(true, 1024, 0, 5, 7),
            Read(
                (HttpHardeningOptions.TrustForwardedHeadersKey, "true"),
                (HttpHardeningOptions.MaxRequestBytesKey, "1024"),
                (HttpHardeningOptions.ApiRequestsPerMinuteKey, "0"),
                (HttpHardeningOptions.UploadsPerMinuteKey, "5"),
                (HttpHardeningOptions.WebhooksPerMinuteKey, "7")));

    [Theory]
    [InlineData(HttpHardeningOptions.MaxRequestBytesKey, "0")]
    [InlineData(HttpHardeningOptions.ApiRequestsPerMinuteKey, "-1")]
    [InlineData(HttpHardeningOptions.UploadsPerMinuteKey, "-5")]
    [InlineData(HttpHardeningOptions.WebhooksPerMinuteKey, "-1")]
    public void Invalid_values_stop_the_start(string key, string value) =>
        Assert.Throws<InvalidOperationException>(() => Read((key, value)));
}
