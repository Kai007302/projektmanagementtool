namespace ProjectHub.Api.Modules.Privacy;

/// <summary>
/// Links to the operator's privacy notice (Art. 13 GDPR) and imprint (§ 5 DDG), shown in the web app (ADR 0017).
/// Public, so they can be read before signing in. Each is an https URL or a path on this host, or not set.
/// </summary>
public sealed record LegalInformation(string? PrivacyNoticeUrl, string? ImprintUrl)
{
    public const string PrivacyNoticeUrlKey = "PROJECTHUB_PRIVACY_NOTICE_URL";
    public const string ImprintUrlKey = "PROJECTHUB_IMPRINT_URL";

    public static LegalInformation FromConfiguration(IConfiguration configuration) =>
        new(Link(configuration, PrivacyNoticeUrlKey), Link(configuration, ImprintUrlKey));

    private static string? Link(IConfiguration configuration, string key)
    {
        var value = configuration[key]?.Trim();
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }

        var isPath = value.StartsWith('/') && !value.StartsWith("//", StringComparison.Ordinal) && !value.Contains('\\');
        var isHttps = Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps && uri.UserInfo.Length == 0;
        return isPath || isHttps
            ? value
            : throw new InvalidOperationException($"Configuration value '{key}' must be an https URL or a path starting with '/'.");
    }
}
