using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using ProjectHub.Api.Infrastructure.Documents;
using ProjectHub.Api.Modules.Gantt;

namespace ProjectHub.Api.UnitTests;

public sealed class PdfDocumentTests
{
    private static readonly DateTimeOffset Created = new(2026, 10, 5, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Writes_a_pdf_whose_cross_reference_table_points_at_its_objects()
    {
        var document = new PdfDocument(PdfDocument.A4Long, PdfDocument.A4Short);
        document.AddPage().Text(10, 20, "Hallo", 12, PdfColor.Hex("#000000"));
        document.AddPage();

        var bytes = document.ToBytes("Test", Created);
        var text = Encoding.Latin1.GetString(bytes);

        Assert.StartsWith("%PDF-1.4\n", text, StringComparison.Ordinal);
        Assert.EndsWith("%%EOF\n", text, StringComparison.Ordinal);
        Assert.Contains("/Count 2", text, StringComparison.Ordinal);
        var xref = int.Parse(Regex.Match(text, @"startxref\n(\d+)").Groups[1].Value);
        Assert.StartsWith("xref\n0 10\n", text[xref..], StringComparison.Ordinal);
        var offsets = Regex.Matches(text[xref..], @"(\d{10}) 00000 n").Select(m => int.Parse(m.Groups[1].Value)).ToList();
        Assert.Equal(9, offsets.Count);
        for (var i = 0; i < offsets.Count; i++)
        {
            Assert.StartsWith($"{i + 1} 0 obj\n", text[offsets[i]..], StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Text_is_encoded_as_winansi_so_umlauts_survive_and_unknown_characters_become_question_marks()
    {
        var document = new PdfDocument(200, 100);
        document.AddPage().Text(10, 20, "Prüfung (ß) ✓", 12, PdfColor.Hex("#000000"));

        var content = Contents(document.ToBytes("x", Created)).Single();

        Assert.Contains($"<{Convert.ToHexString(Encoding.Latin1.GetBytes("Prüfung (ß) ?"))}> Tj", content, StringComparison.Ordinal);
        Assert.Contains("10 80 Td", content, StringComparison.Ordinal);
    }

    [Fact]
    public void Fit_shortens_with_an_ellipsis()
    {
        Assert.Equal("Kurz", PdfPage.Fit("Kurz", 100, 10));
        var fitted = PdfPage.Fit("Ein sehr langer Aufgabentitel, der nicht passt", 80, 10);
        Assert.EndsWith("…", fitted, StringComparison.Ordinal);
        Assert.True(PdfPage.TextWidth(fitted, 10) <= 80);
    }

    /// <summary>The decompressed content streams of all pages.</summary>
    internal static List<string> Contents(byte[] pdf)
    {
        var result = new List<string>();
        var text = Encoding.Latin1.GetString(pdf);
        foreach (Match match in Regex.Matches(text, @"/Length (\d+) /Filter /FlateDecode >>\nstream\n"))
        {
            var start = match.Index + match.Length;
            var length = int.Parse(match.Groups[1].Value);
            using var zlib = new ZLibStream(new MemoryStream(pdf, start, length), CompressionMode.Decompress);
            using var reader = new StreamReader(zlib, Encoding.ASCII);
            result.Add(reader.ReadToEnd());
        }

        return result;
    }
}

public sealed class GanttPdfTests
{
    private static readonly DateOnly Today = new(2026, 10, 5);
    private static readonly DateTimeOffset Now = new(2026, 10, 5, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Draws_tasks_milestones_and_page_numbers()
    {
        var parent = Task("Konzept", new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 20), progress: 50);
        var gantt = new GanttResponse(
            Guid.NewGuid(),
            [parent, Task("Entwurf", new DateOnly(2026, 10, 3), new DateOnly(2026, 10, 9), parentId: parent.Id), Task("Irgendwann", null, null)],
            [],
            [new GanttMilestoneResponse(Guid.NewGuid(), "Go-live", new DateOnly(2026, 10, 30), 1)]);

        var pdf = GanttPdf.Render("Relaunch", gantt, Today, Now);
        var content = string.Join('\n', PdfDocumentTests.Contents(pdf));

        foreach (var text in new[] { "Relaunch", "Konzept", "Entwurf", "Irgendwann", "ohne Termin", "Go-live", "Meilensteine", "Seite 1 von 1", "Okt 2026" })
        {
            Assert.Contains(Hex(text), content, StringComparison.Ordinal);
        }

        Assert.Contains("[3 2] 0 d", content, StringComparison.Ordinal);
    }

    [Fact]
    public void Many_tasks_continue_on_further_pages()
    {
        var tasks = Enumerable.Range(1, 80).Select(i => Task($"Aufgabe {i}", Today.AddDays(i), Today.AddDays(i + 3))).ToList();

        var pdf = GanttPdf.Render("Groß", new GanttResponse(Guid.NewGuid(), tasks, [], []), Today, Now);
        var pages = PdfDocumentTests.Contents(pdf);

        Assert.Equal(3, pages.Count);
        Assert.Contains(Hex("Seite 3 von 3"), pages[2], StringComparison.Ordinal);
        Assert.Contains(Hex("Aufgabe 80"), pages[2], StringComparison.Ordinal);
    }

    [Fact]
    public void An_empty_chart_still_gives_one_page()
    {
        var pages = PdfDocumentTests.Contents(GanttPdf.Render("Leer", new GanttResponse(Guid.NewGuid(), [], [], []), Today, Now));

        Assert.Single(pages);
        Assert.Contains(Hex("Keine Aufgaben und Meilensteine."), pages[0], StringComparison.Ordinal);
    }

    private static GanttTask Task(string title, DateOnly? start, DateOnly? due, short progress = 0, Guid? parentId = null) =>
        new(Guid.NewGuid(), parentId, title, "in_progress", null, start, due, progress, 1);

    private static string Hex(string text) => $"<{Convert.ToHexString(Encoding.Latin1.GetBytes(text))}>";
}
