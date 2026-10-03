using System.Globalization;
using System.Text;

namespace ProjectHub.Api.Modules.Calendar;

/// <summary>An all-day calendar entry. <see cref="LastDay"/> is inclusive.</summary>
public sealed record CalendarEntry(string Uid, long Sequence, string Summary, DateOnly FirstDay, DateOnly LastDay, string Link);

/// <summary>
/// Writes a single all-day event as iCalendar (RFC 5545), which Outlook imports as an appointment. The same
/// <see cref="CalendarEntry.Uid"/> with a higher sequence replaces an earlier import.
/// </summary>
public static class CalendarFile
{
    public const string ContentType = "text/calendar; charset=utf-8";
    private const int MaxLineOctets = 75;

    public static byte[] Write(CalendarEntry entry, DateTimeOffset stamp)
    {
        var builder = new StringBuilder();
        void Line(string text) => Fold(builder, text);

        Line("BEGIN:VCALENDAR");
        Line("VERSION:2.0");
        Line("PRODID:-//ProjectHub//ProjectHub//DE");
        Line("CALSCALE:GREGORIAN");
        Line("METHOD:PUBLISH");
        Line("BEGIN:VEVENT");
        Line($"UID:{Escape(entry.Uid)}");
        Line($"DTSTAMP:{stamp.UtcDateTime.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture)}");
        Line($"SEQUENCE:{Math.Clamp(entry.Sequence, 0, int.MaxValue).ToString(CultureInfo.InvariantCulture)}");
        Line($"DTSTART;VALUE=DATE:{Day(entry.FirstDay)}");
        Line($"DTEND;VALUE=DATE:{Day((entry.LastDay < entry.FirstDay ? entry.FirstDay : entry.LastDay).AddDays(1))}");
        Line($"SUMMARY:{Escape(entry.Summary)}");
        Line($"DESCRIPTION:{Escape($"In ProjectHub öffnen: {entry.Link}")}");
        Line($"URL:{entry.Link}");

        // All-day entries for work items should not block the person's free/busy time.
        Line("TRANSP:TRANSPARENT");
        Line("END:VEVENT");
        Line("END:VCALENDAR");
        return Encoding.UTF8.GetBytes(builder.ToString());
    }

    /// <summary>A file name from the title: no path or reserved characters, at most 60 characters.</summary>
    public static string FileName(string title)
    {
        var invalid = Path.GetInvalidFileNameChars().Concat(['"', '<', '>', '|', ':', '*', '?', '\\', '/']).ToHashSet();
        var cleaned = new string(title.Select(c => invalid.Contains(c) || char.IsControl(c) ? ' ' : c).ToArray());
        cleaned = string.Join(' ', cleaned.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        if (cleaned.Length > 60)
        {
            cleaned = cleaned[..60].TrimEnd();
        }

        return (cleaned.Length == 0 ? "termin" : cleaned) + ".ics";
    }

    private static string Day(DateOnly day) => day.ToString("yyyyMMdd", CultureInfo.InvariantCulture);

    /// <summary>TEXT escaping: backslash, semicolon, comma and line breaks; other control characters become spaces.</summary>
    public static string Escape(string text) =>
        new string(text.Select(c => char.IsControl(c) && c is not '\r' and not '\n' ? ' ' : c).ToArray())
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace(";", "\\;", StringComparison.Ordinal)
            .Replace(",", "\\,", StringComparison.Ordinal)
            .Replace("\r\n", "\\n", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal)
            .Replace("\r", "\\n", StringComparison.Ordinal);

    /// <summary>Lines longer than 75 octets continue on the next line after a space, never inside a UTF-8 sequence.</summary>
    private static void Fold(StringBuilder builder, string line)
    {
        var octets = 0;
        var limit = MaxLineOctets;
        foreach (var rune in line.EnumerateRunes())
        {
            var size = rune.Utf8SequenceLength;
            if (octets + size > limit)
            {
                builder.Append("\r\n ");
                octets = 0;
                limit = MaxLineOctets - 1;
            }

            builder.Append(rune.ToString());
            octets += size;
        }

        builder.Append("\r\n");
    }
}
