using System.Globalization;
using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using ProjectHub.Api.Modules.Identity;

namespace ProjectHub.Api.Infrastructure.Http;

/// <summary>Limits and proxy trust of the HTTP pipeline (ADR 0012). Values of 0 switch a rate limit off.</summary>
public sealed record HttpHardeningOptions(
    bool TrustForwardedHeaders,
    long MaxRequestBytes,
    int ApiRequestsPerMinute,
    int UploadsPerMinute,
    int WebhooksPerMinute)
{
    public const string TrustForwardedHeadersKey = "PROJECTHUB_TRUST_FORWARDED_HEADERS";
    public const string MaxRequestBytesKey = "PROJECTHUB_MAX_REQUEST_BYTES";
    public const string ApiRequestsPerMinuteKey = "PROJECTHUB_RATE_LIMIT_PER_MINUTE";
    public const string UploadsPerMinuteKey = "PROJECTHUB_UPLOAD_RATE_LIMIT_PER_MINUTE";
    public const string WebhooksPerMinuteKey = "PROJECTHUB_WEBHOOK_RATE_LIMIT_PER_MINUTE";

    public const long DefaultMaxRequestBytes = 4 * 1024 * 1024;

    public static HttpHardeningOptions FromConfiguration(IConfiguration configuration)
    {
        var options = new HttpHardeningOptions(
            configuration.GetValue(TrustForwardedHeadersKey, false),
            configuration.GetValue(MaxRequestBytesKey, DefaultMaxRequestBytes),
            configuration.GetValue(ApiRequestsPerMinuteKey, 600),
            configuration.GetValue(UploadsPerMinuteKey, 30),
            configuration.GetValue(WebhooksPerMinuteKey, 120));

        if (options.MaxRequestBytes <= 0 || options.ApiRequestsPerMinute < 0 || options.UploadsPerMinute < 0 || options.WebhooksPerMinute < 0)
        {
            throw new InvalidOperationException(
                $"{MaxRequestBytesKey} must be positive; {ApiRequestsPerMinuteKey}, {UploadsPerMinuteKey} and {WebhooksPerMinuteKey} must not be negative.");
        }

        return options;
    }
}

public static class HttpHardening
{
    public const string UploadPolicy = "uploads";
    public const string WebhookPolicy = "webhooks";

    public static IServiceCollection AddHttpHardening(this IServiceCollection services, IConfiguration configuration)
    {
        var options = HttpHardeningOptions.FromConfiguration(configuration);
        services.AddSingleton(options);

        // Endpoints that need more (uploads) raise it with IRequestSizeLimitMetadata.
        services.Configure<KestrelServerOptions>(kestrel => kestrel.Limits.MaxRequestBodySize = options.MaxRequestBytes);

        services.Configure<ForwardedHeadersOptions>(forwarded =>
        {
            forwarded.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;

            // The ingress address is not known in advance (App Service, Container Apps). Trusting any sender is only
            // safe because the setting is documented as "the API is reachable through the proxy only".
            forwarded.KnownIPNetworks.Clear();
            forwarded.KnownProxies.Clear();
            forwarded.ForwardLimit = 1;
        });

        services.AddHsts(hsts => hsts.MaxAge = TimeSpan.FromDays(365));

        services.AddRateLimiter(limiter =>
        {
            limiter.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
                IsUnlimited(context.Request.Path) ? RateLimitPartition.GetNoLimiter("unlimited") : PerMinute(PartitionKey(context), options.ApiRequestsPerMinute));
            limiter.AddPolicy(UploadPolicy, context => PerMinute(PartitionKey(context), options.UploadsPerMinute));
            limiter.AddPolicy(WebhookPolicy, context => PerMinute(ClientAddress(context), options.WebhooksPerMinute));
            limiter.OnRejected = async (rejected, ct) =>
            {
                var response = rejected.HttpContext.Response;
                if (rejected.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    response.Headers.RetryAfter = Math.Ceiling(retryAfter.TotalSeconds).ToString(CultureInfo.InvariantCulture);
                }

                response.StatusCode = StatusCodes.Status429TooManyRequests;
                await Results.Problem(statusCode: StatusCodes.Status429TooManyRequests, title: "Too many requests.")
                    .ExecuteAsync(rejected.HttpContext);
            };
        });

        return services;
    }

    /// <summary>Must run first, so that every later step sees the client address and scheme from the proxy.</summary>
    public static WebApplication UseForwardedHeadersWhenTrusted(this WebApplication app)
    {
        if (app.Services.GetRequiredService<HttpHardeningOptions>().TrustForwardedHeaders)
        {
            app.UseForwardedHeaders();
        }

        return app;
    }

    /// <summary>
    /// Headers for every response of the API. It only serves JSON, files as downloads and hubs, so nothing may be
    /// rendered, framed or cached. The web app's own CSP is set by the web container (src/ProjectHub.Web/nginx).
    /// </summary>
    public static WebApplication UseSecurityHeaders(this WebApplication app)
    {
        app.Use(async (context, next) =>
        {
            context.Response.OnStarting(() =>
            {
                var headers = context.Response.Headers;
                headers.XContentTypeOptions = "nosniff";
                headers.XFrameOptions = "DENY";
                headers.ContentSecurityPolicy = "default-src 'none'; frame-ancestors 'none'";
                headers["Referrer-Policy"] = "no-referrer";
                headers["Cross-Origin-Resource-Policy"] = "same-origin";
                if (!headers.ContainsKey("Cache-Control"))
                {
                    headers.CacheControl = "no-store";
                }

                return Task.CompletedTask;
            });
            await next(context);
        });

        if (!app.Environment.IsDevelopment())
        {
            app.UseHsts();
        }

        return app;
    }

    /// <summary>A fixed window per signed-in person (otherwise per client address), for modules with their own limit.</summary>
    public static RateLimitPartition<string> PerPersonPerMinute(HttpContext context, int permits) => PerMinute(PartitionKey(context), permits);

    private static bool IsUnlimited(PathString path) => path.StartsWithSegments("/health");

    private static RateLimitPartition<string> PerMinute(string key, int permits) =>
        permits == 0
            ? RateLimitPartition.GetNoLimiter(key)
            : RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = permits,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
            });

    /// <summary>The signed-in person (tenant and object id from the token), otherwise the client address.</summary>
    private static string PartitionKey(HttpContext context) =>
        context.User.FindFirstValue(IdentityClaimTypes.ObjectId) is { } objectId
            ? $"user:{context.User.FindFirstValue(IdentityClaimTypes.TenantId)}:{objectId}"
            : ClientAddress(context);

    private static string ClientAddress(HttpContext context) => $"ip:{context.Connection.RemoteIpAddress}";
}
