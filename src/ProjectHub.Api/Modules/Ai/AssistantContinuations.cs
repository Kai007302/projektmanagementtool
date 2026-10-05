using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Caching.Memory;
using ProjectHub.Api.Modules.Identity;

namespace ProjectHub.Api.Modules.Ai;

/// <summary>
/// A turn of the assistant that waits for the person's approval (ADR 0016). ProjectHub stores no conversations (DEC-035), so the
/// turn so far (the model's messages, tool calls and results) travels to the browser and back, encrypted and signed for this person,
/// valid for <see cref="Lifetime"/> and usable once: the browser can neither read nor change what is approved, nor approve it twice.
/// </summary>
public sealed class AssistantContinuations(IDataProtectionProvider dataProtection, IMemoryCache cache, TimeProvider clock)
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(30);

    private static readonly Lock UseLock = new();

    private sealed record Payload(Guid Id, List<ChatMessage> Messages);

    public string Protect(UserContext user, IEnumerable<ChatMessage> turn)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(new Payload(Guid.CreateVersion7(), turn.ToList()), AIJsonUtilities.DefaultOptions);
        using var compressed = new MemoryStream();
        using (var gzip = new GZipStream(compressed, CompressionLevel.Fastest, leaveOpen: true))
        {
            gzip.Write(json);
        }

        return WebEncoders.Base64UrlEncode(Protector(user).Protect(compressed.ToArray(), clock.GetUtcNow() + Lifetime));
    }

    /// <summary>The turn, or null when the token is invalid, expired, for someone else or already used.</summary>
    public List<ChatMessage>? Unprotect(UserContext user, string token)
    {
        Payload? payload;
        try
        {
            var bytes = Protector(user).Unprotect(WebEncoders.Base64UrlDecode(token), out _);
            using var gzip = new GZipStream(new MemoryStream(bytes), CompressionMode.Decompress);
            payload = JsonSerializer.Deserialize<Payload>(gzip, AIJsonUtilities.DefaultOptions);
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException or JsonException or InvalidDataException)
        {
            return null;
        }

        if (payload is null)
        {
            return null;
        }

        lock (UseLock)
        {
            if (cache.TryGetValue(CacheKey(payload.Id), out _))
            {
                return null;
            }

            cache.Set(CacheKey(payload.Id), true, Lifetime);
        }

        return payload.Messages;
    }

    private ITimeLimitedDataProtector Protector(UserContext user) =>
        dataProtection.CreateProtector("ProjectHub.Ai.Continuation.v1", user.OrganizationId.ToString(), user.UserId.ToString()).ToTimeLimitedDataProtector();

    private static string CacheKey(Guid id) => $"ai-continuation:{id}";
}
