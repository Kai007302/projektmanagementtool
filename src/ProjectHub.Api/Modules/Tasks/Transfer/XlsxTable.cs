using System.Globalization;
using System.IO.Compression;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

namespace ProjectHub.Api.Modules.Tasks.Transfer;

/// <summary>A typed cell for <see cref="XlsxTable.Write"/>: text, a number or a date (shown as date in Excel).</summary>
public abstract record SheetCell
{
    public sealed record Text(string Value) : SheetCell;

    public sealed record Number(decimal Value) : SheetCell;

    public sealed record Day(DateOnly Value) : SheetCell;

    public static SheetCell? Of(string? value) => value is null ? null : new Text(value);
}

/// <summary>
/// Excel workbooks (.xlsx) through the Open XML SDK. Writing produces one sheet with a bold, frozen header row and
/// a filter; reading takes the first sheet and turns every cell into text (numbers invariant, dates as serial numbers).
/// </summary>
public static class XlsxTable
{
    public const string ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    private const uint HeaderStyle = 1;
    private const uint DateStyle = 2;

    /// <summary>Uncompressed size and part count a workbook may have before it is opened (zip bombs).</summary>
    private const long MaxUncompressedBytes = 50L * 1024 * 1024;
    private const int MaxParts = 200;

    public static byte[] Write(string sheetName, IReadOnlyList<string> header, IReadOnlyList<IReadOnlyList<SheetCell?>> rows, IReadOnlyList<double> columnWidths)
    {
        using var stream = new MemoryStream();
        using (var document = SpreadsheetDocument.Create(stream, SpreadsheetDocumentType.Workbook))
        {
            var workbookPart = document.AddWorkbookPart();
            workbookPart.Workbook = new Workbook();
            var stylesPart = workbookPart.AddNewPart<WorkbookStylesPart>();
            stylesPart.Stylesheet = Styles();

            var sheetPart = workbookPart.AddNewPart<WorksheetPart>();
            var data = new SheetData();
            data.Append(Row(1, header.Select(h => (SheetCell?)new SheetCell.Text(h)).ToList(), HeaderStyle));
            for (var i = 0; i < rows.Count; i++)
            {
                data.Append(Row((uint)i + 2, rows[i], null));
            }

            var lastColumn = ColumnName(header.Count - 1);
            sheetPart.Worksheet = new Worksheet(
                new SheetViews(new SheetView(new Pane
                {
                    VerticalSplit = 1, TopLeftCell = "A2", ActivePane = PaneValues.BottomLeft, State = PaneStateValues.Frozen,
                }) { WorkbookViewId = 0 }),
                new Columns(columnWidths.Select((width, i) => new Column { Min = (uint)i + 1, Max = (uint)i + 1, Width = width, CustomWidth = true })),
                data,
                new AutoFilter { Reference = $"A1:{lastColumn}{rows.Count + 1}" });

            workbookPart.Workbook.AppendChild(new Sheets(new Sheet
            {
                Id = workbookPart.GetIdOfPart(sheetPart), SheetId = 1, Name = sheetName,
            }));
            workbookPart.Workbook.AppendChild(new DefinedNames(new DefinedName($"'{sheetName}'!$A$1:${lastColumn}${rows.Count + 1}")
            {
                Name = "_xlnm._FilterDatabase", LocalSheetId = 0, Hidden = true,
            }));
        }

        return stream.ToArray();
    }

    /// <summary>The rows of the first sheet, the header first; empty rows are left out.</summary>
    public static IReadOnlyList<IReadOnlyList<string>> Read(byte[] content, TableLimits limits)
    {
        EnsureReasonableSize(content);
        try
        {
            using var stream = new MemoryStream(content, writable: false);
            using var document = SpreadsheetDocument.Open(stream, false);
            var workbookPart = document.WorkbookPart ?? throw new TableFormatException("The file contains no workbook.");
            var sheet = workbookPart.Workbook?.Sheets?.Elements<Sheet>().FirstOrDefault()
                        ?? throw new TableFormatException("The workbook contains no sheet.");
            if (sheet.Id?.Value is not { } partId || workbookPart.GetPartById(partId) is not WorksheetPart sheetPart)
            {
                throw new TableFormatException("The first sheet is not a worksheet.");
            }

            var sharedStrings = workbookPart.SharedStringTablePart?.SharedStringTable?.Elements<SharedStringItem>().Select(i => i.InnerText).ToList() ?? [];
            var rows = new List<IReadOnlyList<string>>();
            foreach (var row in sheetPart.Worksheet?.Descendants<Row>() ?? [])
            {
                var cells = new List<string>();
                foreach (var cell in row.Elements<Cell>())
                {
                    var column = cell.CellReference?.Value is { } reference ? ColumnIndex(reference) : cells.Count;
                    if (column >= limits.MaxColumns)
                    {
                        throw new TableFormatException($"At most {limits.MaxColumns} columns.");
                    }

                    while (cells.Count < column)
                    {
                        cells.Add(string.Empty);
                    }

                    var value = Value(cell, sharedStrings);
                    if (value.Length > limits.MaxCellLength)
                    {
                        throw new TableFormatException($"A cell has more than {limits.MaxCellLength} characters.");
                    }

                    cells.Add(value);
                }

                if (cells.Any(c => c.Length > 0))
                {
                    if (rows.Count > limits.MaxRows)
                    {
                        throw new TableFormatException($"At most {limits.MaxRows} rows.");
                    }

                    rows.Add(cells);
                }
            }

            return rows;
        }
        catch (Exception e) when (e is OpenXmlPackageException or InvalidDataException or FileFormatException or ArgumentException)
        {
            throw new TableFormatException("The file is not a readable Excel workbook (.xlsx).");
        }
    }

    private static void EnsureReasonableSize(byte[] content)
    {
        try
        {
            using var archive = new ZipArchive(new MemoryStream(content, writable: false), ZipArchiveMode.Read);
            if (archive.Entries.Count > MaxParts || archive.Entries.Sum(e => e.Length) > MaxUncompressedBytes)
            {
                throw new TableFormatException("The workbook is too large.");
            }
        }
        catch (InvalidDataException)
        {
            throw new TableFormatException("The file is not a readable Excel workbook (.xlsx).");
        }
    }

    private static string Value(Cell cell, IReadOnlyList<string> sharedStrings)
    {
        if (cell.DataType?.Value == CellValues.InlineString)
        {
            return cell.InlineString?.InnerText ?? string.Empty;
        }

        var raw = cell.CellValue?.Text ?? string.Empty;
        if (cell.DataType?.Value == CellValues.SharedString)
        {
            return int.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out var index) && index < sharedStrings.Count
                ? sharedStrings[index]
                : string.Empty;
        }

        if (cell.DataType?.Value == CellValues.Boolean)
        {
            return raw == "1" ? "TRUE" : "FALSE";
        }

        return raw;
    }

    private static Row Row(uint index, IReadOnlyList<SheetCell?> values, uint? style)
    {
        var row = new Row { RowIndex = index };
        for (var i = 0; i < values.Count; i++)
        {
            if (values[i] is not { } value)
            {
                continue;
            }

            var cell = new Cell { CellReference = $"{ColumnName(i)}{index}" };
            switch (value)
            {
                case SheetCell.Text text:
                    cell.DataType = CellValues.InlineString;
                    cell.InlineString = new InlineString(new Text(text.Value) { Space = SpaceProcessingModeValues.Preserve });
                    break;
                case SheetCell.Number number:
                    cell.CellValue = new CellValue(number.Value.ToString(CultureInfo.InvariantCulture));
                    break;
                case SheetCell.Day day:
                    cell.CellValue = new CellValue(day.Value.ToDateTime(TimeOnly.MinValue).ToOADate().ToString(CultureInfo.InvariantCulture));
                    cell.StyleIndex = DateStyle;
                    break;
            }

            if (style is { } s)
            {
                cell.StyleIndex = s;
            }

            row.Append(cell);
        }

        return row;
    }

    /// <summary>Default, bold header and a date format (built-in 14, shown in the reader's locale).</summary>
    private static Stylesheet Styles() =>
        new(
            new Fonts(new Font(new FontSize { Val = 11 }), new Font(new Bold(), new FontSize { Val = 11 })) { Count = 2 },
            new Fills(new Fill(new PatternFill { PatternType = PatternValues.None }), new Fill(new PatternFill { PatternType = PatternValues.Gray125 })) { Count = 2 },
            new Borders(new Border()) { Count = 1 },
            new CellFormats(
                new CellFormat(),
                new CellFormat { FontId = 1, ApplyFont = true },
                new CellFormat { NumberFormatId = 14, ApplyNumberFormat = true }) { Count = 3 });

    public static string ColumnName(int index)
    {
        var name = string.Empty;
        for (var i = index + 1; i > 0; i = (i - 1) / 26)
        {
            name = (char)('A' + ((i - 1) % 26)) + name;
        }

        return name;
    }

    private static int ColumnIndex(string reference)
    {
        var index = 0;
        foreach (var c in reference.TakeWhile(char.IsAsciiLetter))
        {
            index = (index * 26) + (char.ToUpperInvariant(c) - 'A' + 1);
            if (index > 16_384)
            {
                break;
            }
        }

        return index - 1;
    }
}
