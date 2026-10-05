using System.IO.Compression;
using System.Text;
using ProjectHub.Api.Modules.Tasks.Transfer;

namespace ProjectHub.Api.UnitTests;

public sealed class CsvTableTests
{
    private static readonly TableLimits Limits = new(100, 20, 1000);

    [Fact]
    public void Writes_semicolon_separated_utf8_with_byte_order_mark()
    {
        var bytes = CsvTable.Write([["Titel", "Status"], ["Größe; prüfen", "Offen"], ["Zeile\nzwei", "Sag \"Hallo\""]]);

        Assert.Equal([0xEF, 0xBB, 0xBF], bytes[..3]);
        Assert.Equal("Titel;Status\r\n\"Größe; prüfen\";Offen\r\n\"Zeile\nzwei\";\"Sag \"\"Hallo\"\"\"\r\n", Encoding.UTF8.GetString(bytes[3..]));
    }

    [Theory]
    [InlineData("=HYPERLINK(\"http://x\")")]
    [InlineData("+49 30 1234")]
    [InlineData("-Rabatt")]
    [InlineData("@SUM(A1)")]
    public void Text_that_looks_like_a_formula_is_written_as_text_and_read_back_unchanged(string value)
    {
        var bytes = CsvTable.Write([["Titel"], [value]]);

        Assert.Contains("'" + value[0], Encoding.UTF8.GetString(bytes), StringComparison.Ordinal);
        Assert.Equal(value, CsvTable.Read(bytes, Limits)[1][0]);
    }

    [Fact]
    public void Reads_quotes_line_breaks_and_empty_rows()
    {
        var rows = CsvTable.Read(Encoding.UTF8.GetBytes("Titel;Beschreibung\r\n\"A;B\";\"Zeile 1\r\nZeile \"\"2\"\"\"\r\n\r\n;\r\nC;\n"), Limits);

        Assert.Equal(3, rows.Count);
        Assert.Equal(["A;B", "Zeile 1\r\nZeile \"2\""], rows[1]);
        Assert.Equal(["C", ""], rows[2]);
    }

    [Theory]
    [InlineData("Titel,Status\nA,Offen\n")]
    [InlineData("Titel\tStatus\nA\tOffen\n")]
    [InlineData("Titel;Status\nA;Offen")]
    public void Detects_the_separator_from_the_header(string text)
    {
        var rows = CsvTable.Read(Encoding.UTF8.GetBytes(text), Limits);

        Assert.Equal(["A", "Offen"], rows[1]);
    }

    [Fact]
    public void Reads_windows_1252_files_as_saved_by_german_excel()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var bytes = Encoding.GetEncoding(1252).GetBytes("Titel;Priorität\nÜbergabe planen;Hoch\n");

        var rows = CsvTable.Read(bytes, Limits);

        Assert.Equal(["Titel", "Priorität"], rows[0]);
        Assert.Equal("Übergabe planen", rows[1][0]);
    }

    [Fact]
    public void Rejects_open_quotes_and_too_many_rows_or_columns()
    {
        Assert.Throws<TableFormatException>(() => CsvTable.Read(Encoding.UTF8.GetBytes("Titel\n\"offen\n"), Limits));
        Assert.Throws<TableFormatException>(() => CsvTable.Read(Encoding.UTF8.GetBytes("Titel\n" + string.Concat(Enumerable.Repeat("x\n", 101))), Limits));
        Assert.Throws<TableFormatException>(() => CsvTable.Read(Encoding.UTF8.GetBytes(string.Join(';', Enumerable.Repeat("a", 21))), Limits));
        Assert.Throws<TableFormatException>(() => CsvTable.Read(Encoding.UTF8.GetBytes("Titel\n" + new string('x', 1001)), Limits));
    }
}

public sealed class XlsxTableTests
{
    private static readonly TableLimits Limits = new(100, 20, 1000);

    [Fact]
    public void Written_workbook_reads_back_with_dates_as_serial_numbers()
    {
        var bytes = XlsxTable.Write(
            "Aufgaben",
            ["Titel", "Fällig", "Aufwand (h)"],
            [
                [new SheetCell.Text("Übergabe"), new SheetCell.Day(new DateOnly(2026, 11, 30)), new SheetCell.Number(1.5m)],
                [new SheetCell.Text("  Leerzeichen  "), null, null],
            ],
            [30, 12, 12]);

        var rows = XlsxTable.Read(bytes, Limits);

        Assert.Equal(["Titel", "Fällig", "Aufwand (h)"], rows[0]);
        Assert.Equal("Übergabe", rows[1][0]);
        Assert.Equal(new DateOnly(2026, 11, 30), TaskSheet.ParseDate(rows[1][1]));
        Assert.Equal("1.5", rows[1][2]);
        Assert.Equal(["  Leerzeichen  "], rows[2]);
    }

    [Fact]
    public void Files_that_are_not_workbooks_are_rejected()
    {
        Assert.Throws<TableFormatException>(() => XlsxTable.Read(Encoding.UTF8.GetBytes("Titel;Status"), Limits));

        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            using var writer = new StreamWriter(archive.CreateEntry("hallo.txt").Open());
            writer.Write("kein Excel");
        }

        Assert.Throws<TableFormatException>(() => XlsxTable.Read(stream.ToArray(), Limits));
    }

    [Fact]
    public void Archives_that_unpack_to_a_huge_size_are_rejected_before_opening()
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            using var entry = archive.CreateEntry("xl/worksheets/sheet1.xml", CompressionLevel.SmallestSize).Open();
            var zeros = new byte[1024 * 1024];
            for (var i = 0; i < 60; i++)
            {
                entry.Write(zeros);
            }
        }

        var error = Assert.Throws<TableFormatException>(() => XlsxTable.Read(stream.ToArray(), Limits));
        Assert.Contains("too large", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0, "A")]
    [InlineData(25, "Z")]
    [InlineData(26, "AA")]
    [InlineData(701, "ZZ")]
    public void Column_names_follow_excel(int index, string name) => Assert.Equal(name, XlsxTable.ColumnName(index));
}

public sealed class TaskSheetTests
{
    [Fact]
    public void Header_accepts_export_names_english_names_and_ignores_unknown_columns()
    {
        var (columns, ignored) = TaskSheet.MapHeader(["Title", "Zuständig (E-Mail)", "Fälligkeitsdatum", "Kostenstelle", "Übergeordnete Aufgabe", "ID", "Priority"]);

        Assert.Equal(0, columns[TaskColumn.Title]);
        Assert.Equal(1, columns[TaskColumn.AssigneeEmail]);
        Assert.Equal(2, columns[TaskColumn.DueDate]);
        Assert.Equal(6, columns[TaskColumn.Priority]);
        Assert.Equal(["Kostenstelle", "Übergeordnete Aufgabe", "ID"], ignored);
    }

    [Fact]
    public void Row_with_labels_dates_and_numbers_becomes_a_draft()
    {
        var (columns, _) = TaskSheet.MapHeader(["Titel", "Status", "Priorität", "Zuständig", "Start", "Fällig", "Fortschritt (%)", "Aufwand (h)"]);

        var (draft, errors) = TaskSheet.ReadRow(2, [" Texte ", "In Arbeit", "dringend", "ben@example.org", "02.11.2026", "2026-11-04", "50 %", "1,5"], columns);

        Assert.Empty(errors);
        Assert.Equal(new TaskDraft(2, "Texte", null, "in_progress", "urgent", "ben@example.org", new DateOnly(2026, 11, 2), new DateOnly(2026, 11, 4), 50, 1.5m), draft);
    }

    [Fact]
    public void Every_bad_value_of_a_row_is_reported()
    {
        var (columns, _) = TaskSheet.MapHeader(["Titel", "Status", "Priorität", "Fällig", "Fortschritt (%)", "Aufwand (h)"]);

        var (draft, errors) = TaskSheet.ReadRow(7, ["", "Später", "sehr hoch", "31.02.2026", "120", "-2"], columns);

        Assert.Null(draft);
        Assert.Equal(
            [TaskImportErrorCodes.Required, TaskImportErrorCodes.UnknownStatus, TaskImportErrorCodes.UnknownPriority,
             TaskImportErrorCodes.InvalidDate, TaskImportErrorCodes.InvalidNumber, TaskImportErrorCodes.InvalidNumber],
            errors.Select(e => e.Code));
        Assert.All(errors, e => Assert.Equal(7, e.Row));
        Assert.Equal("Titel", errors[0].Column);
    }

    [Theory]
    [InlineData("2026-11-30", 2026, 11, 30)]
    [InlineData("30.11.2026", 2026, 11, 30)]
    [InlineData("1.2.26", 2026, 2, 1)]
    [InlineData("2026-11-30T00:00:00", 2026, 11, 30)]
    [InlineData("46356", 2026, 11, 30)]
    public void Dates_in_common_formats_are_understood(string text, int year, int month, int day) =>
        Assert.Equal(new DateOnly(year, month, day), TaskSheet.ParseDate(text));

    [Theory]
    [InlineData("morgen")]
    [InlineData("2026-13-01")]
    [InlineData("0")]
    public void Other_text_is_no_date(string text) => Assert.Null(TaskSheet.ParseDate(text));

    [Theory]
    [InlineData("1.5", 1.5)]
    [InlineData("1,5", 1.5)]
    [InlineData("8", 8)]
    public void Numbers_take_point_or_comma(string text, double expected) => Assert.Equal((decimal)expected, TaskSheet.ParseNumber(text));

    [Theory]
    [InlineData("1.000,5")]
    [InlineData("acht")]
    public void Ambiguous_numbers_are_rejected(string text) => Assert.Null(TaskSheet.ParseNumber(text));
}
