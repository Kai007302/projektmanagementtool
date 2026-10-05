namespace ProjectHub.Api.Infrastructure.Http;

public static class FileNames
{
    /// <summary>
    /// A download file name (without extension) from a title: no path, reserved or control characters, single spaces,
    /// at most 60 characters; <paramref name="fallback"/> if nothing is left.
    /// </summary>
    public static string Safe(string title, string fallback)
    {
        var invalid = Path.GetInvalidFileNameChars().Concat(['"', '<', '>', '|', ':', '*', '?', '\\', '/']).ToHashSet();
        var cleaned = new string(title.Select(c => invalid.Contains(c) || char.IsControl(c) ? ' ' : c).ToArray());
        cleaned = string.Join(' ', cleaned.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        if (cleaned.Length > 60)
        {
            cleaned = cleaned[..60].TrimEnd();
        }

        return cleaned.Length == 0 ? fallback : cleaned;
    }
}
