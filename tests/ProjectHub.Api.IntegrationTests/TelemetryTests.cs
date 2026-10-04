using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Trace;
using ProjectHub.Api.Infrastructure.Observability;
using static ProjectHub.Api.Modules.Identity.Development.DevelopmentSeedData;

namespace ProjectHub.Api.IntegrationTests;

/// <summary>OpenTelemetry only runs with a collector address, and never exports query strings (ADR 0013).</summary>
[Collection(InfrastructureCollection.Name)]
public sealed class TelemetryTests(InfrastructureFixture infrastructure)
{
    private ProjectHubApiFactory Factory(string? otlpEndpoint) =>
        new(infrastructure.Postgres.GetConnectionString(), infrastructure.Redis.GetConnectionString(), configure: builder =>
        {
            if (otlpEndpoint is not null)
            {
                builder.UseSetting(Telemetry.OtlpEndpointKey, otlpEndpoint);
            }
        });

    [Fact]
    public async Task Without_a_collector_nothing_is_collected()
    {
        await using var factory = Factory(null);

        Assert.Null(factory.Services.GetService<TracerProvider>());
    }

    [Fact]
    public async Task Request_traces_carry_the_route_but_not_the_query_string()
    {
        // Nothing listens on this port; the exporter drops what it cannot send.
        await using var factory = Factory("http://127.0.0.1:1");
        Assert.NotNull(factory.Services.GetService<TracerProvider>());
        var stopped = new ConcurrentQueue<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "Microsoft.AspNetCore",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = stopped.Enqueue,
        };
        ActivitySource.AddActivityListener(listener);

        await factory.CreateClientFor(Ada).GetAsync("/api/v1/projects?limit=5&secret=abc");

        var request = Assert.Single(stopped, a => (a.GetTagItem("url.path") as string) == "/api/v1/projects");
        Assert.StartsWith("/api/v1/projects", request.GetTagItem("http.route") as string);
        Assert.Null(request.GetTagItem("url.query"));
    }

    [Fact]
    public void Own_metrics_carry_channel_provider_and_outcome_only()
    {
        var measurements = new ConcurrentQueue<(string Name, Dictionary<string, object?> Tags)>();
        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, l) =>
            {
                if (instrument.Meter.Name == ProjectHubMetrics.MeterName)
                {
                    l.EnableMeasurementEvents(instrument);
                }
            },
        };
        listener.SetMeasurementEventCallback<long>((instrument, _, tags, _) =>
            measurements.Enqueue((instrument.Name, tags.ToArray().ToDictionary(t => t.Key, t => t.Value))));
        listener.Start();

        ProjectHubMetrics.NotificationDelivered("webex", "sent");
        ProjectHubMetrics.WebhookReceived("webex", "duplicate");

        Assert.Contains(measurements, m => m.Name == "projecthub.notifications.delivered"
            && m.Tags.Count == 2 && (string?)m.Tags["channel"] == "webex" && (string?)m.Tags["outcome"] == "sent");
        Assert.Contains(measurements, m => m.Name == "projecthub.webhooks.received"
            && m.Tags.Count == 2 && (string?)m.Tags["provider"] == "webex" && (string?)m.Tags["outcome"] == "duplicate");
    }
}
