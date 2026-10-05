using System.Text;

namespace ProjectHub.Api.Modules.Tasks.Transfer;

/// <summary>
/// CSV as Excel writes and reads it in Germany: semicolon as separator, UTF-8 with byte order mark, CRLF line ends.
/// Reading also accepts comma and tab as separator and files saved as "CSV (Trennzeichen-getrennt)" in Windows-1252.
/// </summary>
public static class CsvTable
{
    public const string ContentType = "text/csv; charset=utf-8";
    public const char Separator = ';';

    private static readonly char[] Separators = [';', ',', '\t'];

    /// <summary>
    /// Free text that starts like a formula gets a leading apostrophe, so a spreadsheet shows it as text instead of
    /// running it (CSV injection). <see cref="Read"/> removes the apostrophe again.
    /// </summary>
    private static readonly char[] FormulaStarts = ['=', '+', '-', '@', '\t', '\r'];

    public static byte[] Write(IReadOnlyList<IReadOnlyList<string?>> rows)
    {
        var builder = new StringBuilder();
        foreach (var row in rows)
        {
            for (var i = 0; i < row.Count; i++)
            {
                if (i > 0)
                {
                    builder.Append(Separator);
                }

                builder.Append(Quote(Defuse(row[i] ?? string.Empty)));
            }

            builder.Append("\r\n");
        }

        return [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(builder.ToString())];
    }

    /// <summary>All rows of the file, the header first. Fails with <see cref="TableFormatException"/> on broken quoting.</summary>
    public static IReadOnlyList<IReadOnlyList<string>> Read(byte[] content, TableLimits limits)
    {
        var text = Decode(content);
        var separator = DetectSeparator(text);
        var rows = new List<IReadOnlyList<string>>();
        var row = new List<string>();
        var cell = new StringBuilder();
        var quoted = false;
        var cellStart = true;

        void EndCell()
        {
            if (row.Count >= limits.MaxColumns)
            {
                throw new TableFormatException($"At most {limits.MaxColumns} columns.");
            }

            row.Add(Restore(cell.ToString()));
            cell.Clear();
            cellStart = true;
        }

        void EndRow()
        {
            EndCell();
            if (row.Any(c => c.Length > 0))
            {
                if (rows.Count > limits.MaxRows)
                {
                    throw new TableFormatException($"At most {limits.MaxRows} rows.");
                }

                rows.Add(row);
            }

            row = [];
        }

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (quoted)
            {
                if (c != '"')
                {
                    cell.Append(c);
                }
                else if (i + 1 < text.Length && text[i + 1] == '"')
                {
                    cell.Append('"');
                    i++;
                }
                else
                {
                    quoted = false;
                }
            }
            else if (c == '"' && cellStart)
            {
                quoted = true;
                cellStart = false;
            }
            else if (c == separator)
            {
                EndCell();
            }
            else if (c is '\r' or '\n')
            {
                if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                {
                    i++;
                }

                EndRow();
            }
            else
            {
                cell.Append(c);
                cellStart = false;
            }

            if (cell.Length > limits.MaxCellLength)
            {
                throw new TableFormatException($"A cell has more than {limits.MaxCellLength} characters.");
            }
        }

        if (quoted)
        {
            throw new TableFormatException("A quoted cell is not closed.");
        }

        if (cell.Length > 0 || row.Count > 0)
        {
            EndRow();
        }

        return rows;
    }

    /// <summary>UTF-8 (with or without byte order mark); anything that is not valid UTF-8 is read as Windows-1252.</summary>
    private static string Decode(byte[] content)
    {
        var preamble = Encoding.UTF8.GetPreamble();
        if (content.AsSpan().StartsWith(preamble))
        {
            return new UTF8Encoding(false, true).GetString(content, preamble.Length, content.Length - preamble.Length);
        }

        try
        {
            return new UTF8Encoding(false, true).GetString(content);
        }
        catch (DecoderFallbackException)
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding(1252).GetString(content);
        }
    }

    /// <summary>The separator that occurs most often in the first line outside quotes; semicolon if none does.</summary>
    private static char DetectSeparator(string text)
    {
        var counts = new Dictionary<char, int>();
        var quoted = false;
        foreach (var c in text)
        {
            if (c == '"')
            {
                quoted = !quoted;
            }
            else if (!quoted && c is '\r' or '\n')
            {
                break;
            }
            else if (!quoted && Separators.Contains(c))
            {
                counts[c] = counts.GetValueOrDefault(c) + 1;
            }
        }

        return counts.Count == 0 ? Separator : counts.MaxBy(p => (p.Value, p.Key == Separator)).Key;
    }

    private static string Quote(string value) =>
        value.IndexOfAny([Separator, '"', '\r', '\n']) >= 0 || value != value.Trim()
            ? $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\""
            : value;

    private static string Defuse(string value) =>
        value.Length > 0 && FormulaStarts.Contains(value[0]) ? "'" + value : value;

    private static string Restore(string value) =>
        value.Length > 1 && value[0] == '\'' && FormulaStarts.Contains(value[1]) ? value[1..] : value;
}

/// <summary>Bounds for reading an uploaded table, so a file cannot exhaust memory.</summary>
public sealed record TableLimits(int MaxRows, int MaxColumns, int MaxCellLength);

/// <summary>The file is not a readable table (broken quoting, too large, not a workbook).</summary>
public sealed class TableFormatException(string message) : Exception(message);
