using System.Globalization;
using System.IO.Compression;
using System.Text;

namespace ProjectHub.Api.Infrastructure.Documents;

/// <summary>An RGB colour with components from 0 to 255.</summary>
public readonly record struct PdfColor(byte R, byte G, byte B)
{
    public static PdfColor Hex(string hex) =>
        new(Convert.ToByte(hex[1..3], 16), Convert.ToByte(hex[3..5], 16), Convert.ToByte(hex[5..7], 16));

    internal string Operands =>
        string.Create(CultureInfo.InvariantCulture, $"{R / 255.0:0.###} {G / 255.0:0.###} {B / 255.0:0.###}");
}

/// <summary>
/// A small PDF 1.4 writer for reports drawn from rectangles, lines and text (ADR 0018), so no PDF library with its
/// licence terms is needed. Text uses the standard fonts Helvetica and Helvetica-Bold in WinAnsi encoding, which every
/// PDF reader has built in and which covers German; other characters become "?". Coordinates are in points
/// (1/72 inch) from the top left corner of the page.
/// </summary>
public sealed class PdfDocument(double pageWidth, double pageHeight)
{
    /// <summary>A4 landscape.</summary>
    public const double A4Long = 841.89;
    public const double A4Short = 595.28;

    private readonly List<PdfPage> pages = [];

    public double PageWidth { get; } = pageWidth;
    public double PageHeight { get; } = pageHeight;
    public int PageCount => pages.Count;

    public PdfPage AddPage()
    {
        var page = new PdfPage(PageHeight);
        pages.Add(page);
        return page;
    }

    public byte[] ToBytes(string title, DateTimeOffset created)
    {
        if (pages.Count == 0)
        {
            AddPage();
        }

        // Objects: 1 catalog, 2 page tree, 3 Helvetica, 4 Helvetica-Bold, 5 info, then per page the page and its content.
        var objects = new List<byte[]>
        {
            Ascii("<< /Type /Catalog /Pages 2 0 R >>"),
            Ascii($"<< /Type /Pages /Kids [{string.Join(' ', pages.Select((_, i) => $"{6 + (2 * i)} 0 R"))}] /Count {pages.Count} >>"),
            Ascii("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>"),
            Ascii("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold /Encoding /WinAnsiEncoding >>"),
            Ascii($"<< /Title {PdfPage.HexString(title)} /Producer (ProjectHub) /CreationDate (D:{created.UtcDateTime:yyyyMMddHHmmss}Z) >>"),
        };

        var size = string.Create(CultureInfo.InvariantCulture, $"{PageWidth:0.##} {PageHeight:0.##}");
        for (var i = 0; i < pages.Count; i++)
        {
            objects.Add(Ascii(
                $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {size}] /Resources << /Font << /F1 3 0 R /F2 4 0 R >> >> /Contents {7 + (2 * i)} 0 R >>"));
            objects.Add(Stream(pages[i].Content()));
        }

        using var output = new MemoryStream();
        Write(output, "%PDF-1.4\n%âãÏÓ\n", Encoding.Latin1);
        var offsets = new List<long>();
        for (var i = 0; i < objects.Count; i++)
        {
            offsets.Add(output.Position);
            Write(output, $"{i + 1} 0 obj\n");
            output.Write(objects[i]);
            Write(output, "\nendobj\n");
        }

        var xref = output.Position;
        var table = new StringBuilder($"xref\n0 {objects.Count + 1}\n0000000000 65535 f \n");
        foreach (var offset in offsets)
        {
            table.Append(CultureInfo.InvariantCulture, $"{offset:D10} 00000 n \n");
        }

        table.Append(CultureInfo.InvariantCulture, $"trailer\n<< /Size {objects.Count + 1} /Root 1 0 R /Info 5 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        Write(output, table.ToString());
        return output.ToArray();
    }

    private static byte[] Stream(string content)
    {
        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
        {
            zlib.Write(Encoding.ASCII.GetBytes(content));
        }

        var data = compressed.ToArray();
        return [.. Ascii($"<< /Length {data.Length} /Filter /FlateDecode >>\nstream\n"), .. data, .. Ascii("\nendstream")];
    }

    private static byte[] Ascii(string text) => Encoding.ASCII.GetBytes(text);

    private static void Write(Stream stream, string text, Encoding? encoding = null) => stream.Write((encoding ?? Encoding.ASCII).GetBytes(text));
}

public sealed class PdfPage
{
    private static readonly Encoding WinAnsi = CreateWinAnsi();
    private readonly StringBuilder content = new();
    private readonly double height;

    internal PdfPage(double height) => this.height = height;

    public void FillRect(double x, double y, double width, double height, PdfColor color) =>
        Append($"{color.Operands} rg {N(x)} {N(Y(y + height))} {N(width)} {N(height)} re f");

    public void StrokeRect(double x, double y, double width, double height, PdfColor color, double lineWidth = 0.5) =>
        Append($"{color.Operands} RG {N(lineWidth)} w [] 0 d {N(x)} {N(Y(y + height))} {N(width)} {N(height)} re S");

    public void Line(double x1, double y1, double x2, double y2, PdfColor color, double lineWidth = 0.5, bool dashed = false) =>
        Append($"{color.Operands} RG {N(lineWidth)} w {(dashed ? "[3 2]" : "[]")} 0 d {N(x1)} {N(Y(y1))} m {N(x2)} {N(Y(y2))} l S");

    public void Polygon(IReadOnlyList<(double X, double Y)> points, PdfColor fill)
    {
        var path = new StringBuilder();
        for (var i = 0; i < points.Count; i++)
        {
            path.Append(CultureInfo.InvariantCulture, $"{N(points[i].X)} {N(Y(points[i].Y))} {(i == 0 ? "m" : "l")} ");
        }

        Append($"{fill.Operands} rg {path}h f");
    }

    /// <summary>Text with its baseline at <paramref name="y"/>.</summary>
    public void Text(double x, double y, string text, double size, PdfColor color, bool bold = false) =>
        Append($"BT {color.Operands} rg /{(bold ? "F2" : "F1")} {N(size)} Tf {N(x)} {N(Y(y))} Td {HexString(text)} Tj ET");

    /// <summary>Approximate width of a text in Helvetica (bold slightly wider).</summary>
    public static double TextWidth(string text, double size, bool bold = false) =>
        text.Sum(c => (double)Advance(c)) * size / 1000 * (bold ? 1.05 : 1);

    /// <summary>The text, shortened with "…" so that it fits into <paramref name="maxWidth"/>.</summary>
    public static string Fit(string text, double maxWidth, double size, bool bold = false)
    {
        if (TextWidth(text, size, bold) <= maxWidth)
        {
            return text;
        }

        var length = text.Length;
        while (length > 0 && TextWidth(text[..length] + "…", size, bold) > maxWidth)
        {
            length--;
        }

        return length == 0 ? string.Empty : text[..length].TrimEnd() + "…";
    }

    /// <summary>A string as hexadecimal WinAnsi bytes, which needs no escaping.</summary>
    internal static string HexString(string text) => $"<{Convert.ToHexString(WinAnsi.GetBytes(text))}>";

    internal string Content() => content.ToString();

    private double Y(double y) => height - y;

    private void Append(string operation) => content.Append(operation).Append('\n');

    private static string N(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    private static Encoding CreateWinAnsi()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(1252, new EncoderReplacementFallback("?"), DecoderFallback.ReplacementFallback);
    }

    /// <summary>Helvetica advance widths (per 1000 units) for printable ASCII; other characters count as a digit.</summary>
    private static int Advance(char c) => c switch
    {
        >= ' ' and <= '~' => AsciiWidths[c - ' '],
        'ä' or 'ö' or 'ü' => 556,
        'Ä' => 667,
        'Ö' => 778,
        'Ü' => 722,
        'ß' => 611,
        '…' => 1000,
        _ => 556,
    };

    private static readonly int[] AsciiWidths =
    [
        278, 278, 355, 556, 556, 889, 667, 191, 333, 333, 389, 584, 278, 333, 278, 278, // space to /
        556, 556, 556, 556, 556, 556, 556, 556, 556, 556, 278, 278, 584, 584, 584, 556, // 0 to ?
        1015, 667, 667, 722, 722, 667, 611, 778, 722, 278, 500, 667, 556, 833, 722, 778, // @ to O
        667, 778, 722, 667, 611, 722, 667, 944, 667, 667, 611, 278, 278, 278, 469, 556, // P to _
        333, 556, 556, 500, 556, 556, 278, 556, 556, 222, 222, 500, 222, 833, 556, 556, // ` to o
        556, 556, 333, 500, 278, 556, 500, 722, 500, 500, 500, 334, 260, 334, 584, // p to ~
    ];
}
