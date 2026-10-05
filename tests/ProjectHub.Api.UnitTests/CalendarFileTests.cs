using System.Text;
using ProjectHub.Api.Modules.Calendar;

namespace ProjectHub.Api.UnitTests;

public sealed class CalendarFileTests
{
    private static readonly DateTimeOffset Stamp = new(2026, 10, 3, 12, 30, 0, TimeSpan.FromHours(2));

    [Fact]
    public void Writes_an_all_day_event_with_exclusive_end()
    {
        var text = Write(new CalendarEntry("task-1@projecthub", 4, "Texte schreiben", new DateOnly(2026, 10, 5), new DateOnly(2026, 10, 7), "https://app"));

        Assert.StartsWith("BEGIN:VCALENDAR\r\nVERSION:2.0\r\n", text, StringComparison.Ordinal);
        Assert.Contains("\r\nUID:task-1@projecthub\r\n", text, StringComparison.Ordinal);
        Assert.Contains("\r\nDTSTAMP:20261003T103000Z\r\n", text, StringComparison.Ordinal);
        Assert.Contains("\r\nSEQUENCE:4\r\n", text, StringComparison.Ordinal);
        Assert.Contains("\r\nDTSTART;VALUE=DATE:20261005\r\n", text, StringComparison.Ordinal);
        Assert.Contains("\r\nDTEND;VALUE=DATE:20261008\r\n", text, StringComparison.Ordinal);
        Assert.Contains("\r\nSUMMARY:Texte schreiben\r\n", text, StringComparison.Ordinal);
        Assert.Contains("\r\nURL:https://app\r\n", text, StringComparison.Ordinal);
        Assert.EndsWith("END:VEVENT\r\nEND:VCALENDAR\r\n", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Escapes_text_so_titles_cannot_add_properties()
    {
        var text = Write(new CalendarEntry("u", 1, "A;B,C\\D\r\nEND:VEVENT\tX", new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 1), "https://app"));

        Assert.Contains("\r\nSUMMARY:A\\;B\\,C\\\\D\\nEND:VEVENT X\r\n", text, StringComparison.Ordinal);
        Assert.Single(text.Split("\r\n"), line => line == "END:VEVENT");
    }

    [Fact]
    public void Folds_long_lines_without_splitting_characters()
    {
        var title = string.Concat(Enumerable.Repeat("Größenänderung ", 20));
        var bytes = CalendarFile.Write(new CalendarEntry("u", 1, title, new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 1), "https://app"), Stamp);
        var text = Encoding.UTF8.GetString(bytes);

        Assert.All(text.Split("\r\n"), line => Assert.True(Encoding.UTF8.GetByteCount(line) <= 75, line));
        var unfolded = text.Replace("\r\n ", "", StringComparison.Ordinal);
        Assert.Contains($"SUMMARY:{title}\r\n", unfolded, StringComparison.Ordinal);
        Assert.DoesNotContain('�', text);
    }

    [Fact]
    public void A_last_day_before_the_first_becomes_a_single_day()
    {
        var text = Write(new CalendarEntry("u", 1, "x", new DateOnly(2026, 3, 10), new DateOnly(2026, 3, 1), "https://app"));

        Assert.Contains("DTEND;VALUE=DATE:20260311", text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Texte schreiben", "Texte schreiben.ics")]
    [InlineData("../../etc/passwd", ".. .. etc passwd.ics")]
    [InlineData("a:b*c?\"d<e>f|g", "a b c d e f g.ics")]
    [InlineData("   ", "termin.ics")]
    public void File_names_are_safe(string title, string expected) =>
        Assert.Equal(expected, CalendarFile.FileName(title));

    private static string Write(CalendarEntry entry) => Encoding.UTF8.GetString(CalendarFile.Write(entry, Stamp));

    [Fact]
    public void A_feed_holds_several_events_and_asks_for_hourly_refresh()
    {
        var bytes = CalendarFile.Write(
            [
                new CalendarEntry("task-1@projecthub", 1, "Eins", new DateOnly(2026, 10, 5), new DateOnly(2026, 10, 5), "https://app"),
                new CalendarEntry("milestone-2@projecthub", 2, "Zwei", new DateOnly(2026, 10, 9), new DateOnly(2026, 10, 9), "https://app"),
            ],
            Stamp,
            "ProjectHub – Meine Termine");
        var text = Encoding.UTF8.GetString(bytes);

        Assert.Contains("\r\nX-WR-CALNAME:ProjectHub – Meine Termine\r\nREFRESH-INTERVAL;VALUE=DURATION:PT1H\r\n", text, StringComparison.Ordinal);
        Assert.Equal(2, text.Split("BEGIN:VEVENT").Length - 1);
        Assert.Contains("\r\nUID:milestone-2@projecthub\r\n", text, StringComparison.Ordinal);
    }

    [Fact]
    public void An_empty_feed_is_a_valid_calendar()
    {
        var text = Encoding.UTF8.GetString(CalendarFile.Write([], Stamp, "Leer"));

        Assert.DoesNotContain("BEGIN:VEVENT", text, StringComparison.Ordinal);
        Assert.EndsWith("X-PUBLISHED-TTL:PT1H\r\nEND:VCALENDAR\r\n", text, StringComparison.Ordinal);
    }
}
