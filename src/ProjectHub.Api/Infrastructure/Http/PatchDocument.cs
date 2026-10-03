using System.Text.Json;
using System.Text.Json.Nodes;
using ProjectHub.Api.Infrastructure.Outcomes;

namespace ProjectHub.Api.Infrastructure.Http;

/// <summary>
/// A JSON merge-patch body (RFC 7396): absent properties stay unchanged, null clears a value.
/// </summary>
public sealed class PatchDocument
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    private readonly JsonObject body;

    private PatchDocument(JsonObject body) => this.body = body;

    public static async ValueTask<PatchDocument?> BindAsync(HttpContext context)
    {
        var node = await JsonNode.ParseAsync(context.Request.Body, cancellationToken: context.RequestAborted);
        return node is JsonObject body
            ? new PatchDocument(body)
            : throw new BadHttpRequestException("The request body must be a JSON object.");
    }

    public static PatchDocument From(JsonObject body) => new(body);

    public bool Has(string property) => body.ContainsKey(property);

    /// <summary>Reads a present property; false with an error message when the value has the wrong type.</summary>
    public bool TryGet<T>(string property, out T? value, out string? error)
    {
        value = default;
        error = null;
        if (!body.TryGetPropertyValue(property, out var node) || node is null)
        {
            return true;
        }

        try
        {
            value = node.Deserialize<T>(Options);
            return true;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
        {
            error = "Invalid value.";
            return false;
        }
    }

    /// <summary>The required <c>version</c> the change is based on.</summary>
    public bool TryGetVersion(out long version, out ServiceFailure? error)
    {
        var valid = TryGet<long?>("version", out var value, out _) && value is not null;
        version = value ?? 0;
        error = valid ? null : ServiceFailure.Invalid("version", "Required: the version the change is based on.");
        return valid;
    }

    /// <summary>Applies a present property and records its name in <paramref name="changed"/>.</summary>
    public bool TryApply<T>(string property, Action<T?> apply, List<string> changed, out ServiceFailure? error)
    {
        error = null;
        if (!Has(property))
        {
            return true;
        }

        if (!TryGet<T>(property, out var value, out var message))
        {
            error = ServiceFailure.Invalid(property, message!);
            return false;
        }

        apply(value);
        changed.Add(property);
        return true;
    }
}
