using System.Diagnostics.Metrics;
using Npgsql;
using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace ProjectHub.Api.Infrastructure.Observability;

/// <summary>ProjectHub's own metrics. Tags carry no personal data: only channels, providers and outcomes.</summary>
public static class ProjectHubMetrics
{
    public const string MeterName = "ProjectHub";

    private static readonly Meter Meter = new(MeterName);

    private static readonly Counter<long> Notifications = Meter.CreateCounter<long>(
        "projecthub.notifications.delivered", "{message}", "Outbox deliveries by channel and outcome (sent, retry, failed).");

    private static readonly Counter<long> Webhooks = Meter.CreateCounter<long>(
        "projecthub.webhooks.received", "{delivery}", "Incoming webhooks by provider and outcome.");

    public static void NotificationDelivered(string channel, string outcome) =>
        Notifications.Add(1, new("channel", channel), new("outcome", outcome));

    public static void WebhookReceived(string provider, string outcome) =>
        Webhooks.Add(1, new("provider", provider), new("outcome", outcome));
}

public static class Telemetry
{
    /// <summary>Standard OpenTelemetry variable; without it nothing is collected or exported.</summary>
    public const string OtlpEndpointKey = "OTEL_EXPORTER_OTLP_ENDPOINT";

    public const string ServiceName = "projecthub-api";

    /// <summary>
    /// Logs as JSON outside Development, with trace and span id from the request scope, so a log line and its trace
    /// can be found together. Traces, metrics and logs go to an OTLP collector when <see cref="OtlpEndpointKey"/> is set
    /// (OTEL_SERVICE_NAME, OTEL_EXPORTER_OTLP_HEADERS and the other standard variables apply).
    /// </summary>
    public static WebApplicationBuilder AddProjectHubTelemetry(this WebApplicationBuilder builder)
    {
        if (!builder.Environment.IsDevelopment())
        {
            builder.Logging.AddJsonConsole(json =>
            {
                json.IncludeScopes = true;
                json.UseUtcTimestamp = true;
                json.TimestampFormat = "O";
            });
        }

        if (string.IsNullOrWhiteSpace(builder.Configuration[OtlpEndpointKey]))
        {
            return builder;
        }

        builder.Logging.AddOpenTelemetry(logging =>
        {
            logging.IncludeFormattedMessage = true;
            logging.IncludeScopes = true;
        });

        builder.Services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(ServiceName))
            .WithTracing(tracing => tracing
                .AddAspNetCoreInstrumentation(aspNetCore =>
                {
                    aspNetCore.Filter = context => !context.Request.Path.StartsWithSegments("/health");

                    // Hub connections carry the access token in the query string; queries never leave the process.
                    aspNetCore.EnrichWithHttpRequest = (activity, _) => activity.SetTag("url.query", null);
                })
                .AddHttpClientInstrumentation()
                .AddNpgsql())
            .WithMetrics(metrics => metrics
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddRuntimeInstrumentation()
                .AddMeter("Npgsql", ProjectHubMetrics.MeterName))
            .UseOtlpExporter();

        return builder;
    }
}
