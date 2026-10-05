using System.Globalization;
using ProjectHub.Api.Infrastructure.Documents;

namespace ProjectHub.Api.Modules.Gantt;

/// <summary>
/// Draws the Gantt chart of a project on A4 landscape pages (ADR 0018): the whole time span fits the page width,
/// tasks are listed as in the chart (subtasks indented below their parent) and continue on further pages.
/// </summary>
public static class GanttPdf
{
    private const double Margin = 32;
    private const double LabelWidth = 210;
    private const double ChartTop = 78;
    private const double HeaderHeight = 28;
    private const double RowHeight = 16;
    private const double BarHeight = 9;
    private const double FooterHeight = 24;
    private const double ChartRight = PdfDocument.A4Long - Margin;

    private static readonly string[] Months = ["Jan", "Feb", "Mär", "Apr", "Mai", "Jun", "Jul", "Aug", "Sep", "Okt", "Nov", "Dez"];

    private static readonly PdfColor Text = PdfColor.Hex("#111827");
    private static readonly PdfColor Muted = PdfColor.Hex("#6B7280");
    private static readonly PdfColor Grid = PdfColor.Hex("#E5E7EB");
    private static readonly PdfColor Weekend = PdfColor.Hex("#F3F4F6");
    private static readonly PdfColor Today = PdfColor.Hex("#DC2626");
    private static readonly PdfColor Milestone = PdfColor.Hex("#7C3AED");

    private static readonly IReadOnlyDictionary<string, (PdfColor Track, PdfColor Progress)> StatusColors =
        new Dictionary<string, (PdfColor, PdfColor)>
        {
            ["todo"] = (PdfColor.Hex("#CBD5E1"), PdfColor.Hex("#64748B")),
            ["in_progress"] = (PdfColor.Hex("#BFDBFE"), PdfColor.Hex("#2563EB")),
            ["done"] = (PdfColor.Hex("#BBF7D0"), PdfColor.Hex("#16A34A")),
        };

    public static byte[] Render(string projectName, GanttResponse gantt, DateOnly today, DateTimeOffset now)
    {
        var document = new PdfDocument(PdfDocument.A4Long, PdfDocument.A4Short);
        var rows = Rows(gantt);
        var (first, last) = Range(gantt, today);
        var days = last.DayNumber - first.DayNumber + 1;
        var chartX = Margin + LabelWidth;
        var chartWidth = document.PageWidth - Margin - chartX;
        var dayWidth = chartWidth / days;
        double X(DateOnly day) => chartX + ((day.DayNumber - first.DayNumber) * dayWidth);

        var bodyTop = ChartTop + HeaderHeight;
        var rowsPerPage = Math.Max(1, (int)((document.PageHeight - Margin - FooterHeight - bodyTop) / RowHeight));
        var pageCount = Math.Max(1, (int)Math.Ceiling(rows.Count / (double)rowsPerPage));
        var summary = $"Gantt-Diagramm · Stand {today.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture)} · "
                      + $"{gantt.Tasks.Count} Aufgaben, {gantt.Milestones.Count} Meilensteine";

        for (var pageIndex = 0; pageIndex < pageCount; pageIndex++)
        {
            var page = document.AddPage();
            var pageRows = rows.Skip(pageIndex * rowsPerPage).Take(rowsPerPage).ToList();
            var bodyBottom = bodyTop + (Math.Max(1, pageRows.Count) * RowHeight);

            page.Text(Margin, Margin + 14, PdfPage.Fit(projectName, document.PageWidth - (2 * Margin), 16, bold: true), 16, Text, bold: true);
            page.Text(Margin, Margin + 30, summary, 9, Muted);

            DrawTimeline(page, first, last, dayWidth, X, bodyBottom);
            if (today >= first && today <= last)
            {
                var todayX = X(today) + (dayWidth / 2);
                page.Line(todayX, ChartTop + HeaderHeight, todayX, bodyBottom, Today, 0.8, dashed: true);
            }

            for (var i = 0; i < pageRows.Count; i++)
            {
                var top = bodyTop + (i * RowHeight);
                DrawRow(page, pageRows[i], top, dayWidth, X);
                page.Line(Margin, top + RowHeight, document.PageWidth - Margin, top + RowHeight, Grid, 0.3);
            }

            if (rows.Count == 0)
            {
                page.Text(Margin, bodyTop + 11, "Keine Aufgaben und Meilensteine.", 9, Muted);
            }

            page.Text(Margin, document.PageHeight - Margin + 8, "ProjectHub", 8, Muted);
            var pageText = $"Seite {pageIndex + 1} von {pageCount}";
            page.Text(document.PageWidth - Margin - PdfPage.TextWidth(pageText, 8), document.PageHeight - Margin + 8, pageText, 8, Muted);
        }

        return document.ToBytes($"{projectName} – Gantt", now);
    }

    /// <summary>Month names in the upper header row; days, calendar weeks or nothing below, depending on the space.</summary>
    private static void DrawTimeline(PdfPage page, DateOnly first, DateOnly last, double dayWidth, Func<DateOnly, double> x, double bodyBottom)
    {
        var bodyTop = ChartTop + HeaderHeight;
        if (dayWidth >= 3)
        {
            for (var day = first; day <= last; day = day.AddDays(1))
            {
                if (day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
                {
                    page.FillRect(x(day), bodyTop, dayWidth, bodyBottom - bodyTop, Weekend);
                }
            }
        }

        for (var month = new DateOnly(first.Year, first.Month, 1); month <= last; month = month.AddMonths(1))
        {
            var start = month < first ? first : month;
            var end = month.AddMonths(1).AddDays(-1) is var monthEnd && monthEnd > last ? last : monthEnd;
            var left = x(start);
            var width = x(end) + dayWidth - left;
            page.Line(left, ChartTop, left, bodyBottom, Grid, 0.5);
            var label = width > 60 ? $"{Months[month.Month - 1]} {month.Year}" : Months[month.Month - 1];
            if (PdfPage.TextWidth(label, 8) + 4 <= width)
            {
                page.Text(left + 2, ChartTop + 10, label, 8, Text, bold: true);
            }
        }

        if (dayWidth >= 9)
        {
            for (var day = first; day <= last; day = day.AddDays(1))
            {
                var label = day.Day.ToString(CultureInfo.InvariantCulture);
                page.Text(x(day) + ((dayWidth - PdfPage.TextWidth(label, 6)) / 2), ChartTop + 24, label, 6, Muted);
            }
        }
        else if (dayWidth * 7 >= 24)
        {
            for (var day = first; day <= last; day = day.AddDays(1))
            {
                if (day.DayOfWeek == DayOfWeek.Monday)
                {
                    page.Line(x(day), ChartTop + HeaderHeight - 12, x(day), ChartTop + HeaderHeight, Grid, 0.5);
                    var week = $"KW {ISOWeek.GetWeekOfYear(day.ToDateTime(TimeOnly.MinValue))}";
                    if (x(day) + 2 + PdfPage.TextWidth(week, 6) <= ChartRight)
                    {
                        page.Text(x(day) + 2, ChartTop + 24, week, 6, Muted);
                    }
                }
            }
        }

        page.Line(Margin, ChartTop + HeaderHeight, x(last) + dayWidth, ChartTop + HeaderHeight, Muted, 0.6);
    }

    private static void DrawRow(PdfPage page, Row row, double top, double dayWidth, Func<DateOnly, double> x)
    {
        var baseline = top + 11;
        var center = top + (RowHeight / 2);
        if (row.Milestones is { } milestones)
        {
            page.Text(Margin, baseline, "Meilensteine", 8, Muted);
            foreach (var milestone in milestones)
            {
                var mx = x(milestone.Date) + (dayWidth / 2);
                page.Polygon([(mx, center - 5), (mx + 5, center), (mx, center + 5), (mx - 5, center)], Milestone);
                var name = PdfPage.Fit(milestone.Name, 120, 7);
                var nameWidth = PdfPage.TextWidth(name, 7);

                // Near the right edge the name goes to the left of the diamond.
                page.Text(mx + 7 + nameWidth <= ChartRight ? mx + 7 : mx - 7 - nameWidth, baseline, name, 7, Text);
            }

            return;
        }

        var task = row.Task!;
        var indent = Math.Min(row.Depth, 6) * 10;
        page.Text(Margin + indent, baseline, PdfPage.Fit(task.Title, LabelWidth - indent - 8, 8, bold: row.Depth == 0), 8, Text, bold: row.Depth == 0);
        if (GanttRules.Span(task.StartDate, task.DueDate) is not { } span)
        {
            page.Text(Margin + LabelWidth + 4, baseline, "ohne Termin", 7, Muted);
            return;
        }

        var (track, progress) = StatusColors.GetValueOrDefault(task.Status, StatusColors["todo"]);
        var left = x(span.Start);
        var width = Math.Max(x(span.End) + dayWidth - left, 2);
        var barTop = center - (BarHeight / 2);
        page.FillRect(left, barTop, width, BarHeight, track);
        if (task.Progress > 0)
        {
            page.FillRect(left, barTop, width * Math.Clamp((int)task.Progress, 0, 100) / 100, BarHeight, progress);
        }
    }

    /// <summary>The milestone row (if any) and then the tasks, parents before their subtasks.</summary>
    private static List<Row> Rows(GanttResponse gantt)
    {
        var rows = new List<Row>();
        if (gantt.Milestones.Count > 0)
        {
            rows.Add(new Row(null, 0, gantt.Milestones));
        }

        var ids = gantt.Tasks.Select(t => t.Id).ToHashSet();
        var children = gantt.Tasks.ToLookup(t => t.ParentTaskId is { } parent && ids.Contains(parent) ? parent : (Guid?)null);
        void Visit(Guid? parent, int depth)
        {
            foreach (var task in children[parent])
            {
                rows.Add(new Row(task, depth, null));
                Visit(task.Id, depth + 1);
            }
        }

        Visit(null, 0);
        return rows;
    }

    /// <summary>From the earliest to the latest date (tasks, milestones) with a day of space on both sides.</summary>
    private static (DateOnly First, DateOnly Last) Range(GanttResponse gantt, DateOnly today)
    {
        var dates = gantt.Tasks
            .SelectMany(t => GanttRules.Span(t.StartDate, t.DueDate) is { } span ? new[] { span.Start, span.End } : [])
            .Concat(gantt.Milestones.Select(m => m.Date))
            .ToList();
        if (dates.Count == 0)
        {
            return (today.AddDays(-7), today.AddDays(21));
        }

        return (dates.Min().AddDays(-1), dates.Max().AddDays(1));
    }

    private sealed record Row(GanttTask? Task, int Depth, IReadOnlyList<GanttMilestoneResponse>? Milestones);
}
