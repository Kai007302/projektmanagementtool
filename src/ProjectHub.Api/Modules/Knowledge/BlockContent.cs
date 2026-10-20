using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ProjectHub.Api.Modules.Knowledge;

/// <summary>
/// The canonical article content: <c>{"blocks": [...]}</c> with typed blocks of plain text.
/// HTML is never stored; the client renders blocks itself. Validation keeps unknown fields out,
/// so the stored JSON contains exactly what the schema below describes.
/// </summary>
public static class BlockContent
{
    public const int MaxBlocks = 2000;
    public const int MaxTextLength = 20_000;
    public const int MaxItems = 500;

    /// <summary>Limits of a data table (DEC-046): enough for a series of measurements, small enough to render in the browser.</summary>
    public const int MaxTableColumns = 20;
    public const int MaxTableRows = 500;
    public const int MaxCellLength = 200;
    public const int MaxInsightFields = 12;

    public static readonly IReadOnlyList<string> Types =
    [
        "heading", "paragraph", "bullet_list", "numbered_list", "checklist", "quote", "callout", "code",
        "image", "file", "link", "task_reference", "project_reference", "knowledge_reference", "insight", "data_table",
    ];

    public static readonly IReadOnlyList<string> CalloutTones = ["info", "warning", "success"];

    /// <summary>How a data table is drawn: as a table only, or with a line or bar chart of its number columns.</summary>
    public static readonly IReadOnlyList<string> ChartKinds = ["none", "line", "bar"];

    public static JsonObject Empty() => new() { ["blocks"] = new JsonArray() };

    /// <summary>Ids that reference blocks point to, so the caller can check they exist and are visible.</summary>
    public sealed record BlockReferences(IReadOnlyList<Guid> Tasks, IReadOnlyList<Guid> Projects, IReadOnlyList<Guid> Articles);

    public sealed record Normalized(JsonObject Content, string PlainText, BlockReferences References);

    /// <summary>Validates and normalizes content; returns an error message when it is not acceptable.</summary>
    public static Normalized? Normalize(JsonNode? content, out string? error)
    {
        error = null;
        if (content is not JsonObject root || root["blocks"] is not JsonArray blocks)
        {
            error = "Content must be an object with a 'blocks' array.";
            return null;
        }

        if (blocks.Count > MaxBlocks)
        {
            error = $"At most {MaxBlocks} blocks.";
            return null;
        }

        var normalized = new JsonArray();
        var text = new StringBuilder();
        var tasks = new List<Guid>();
        var projects = new List<Guid>();
        var articles = new List<Guid>();

        for (var i = 0; i < blocks.Count; i++)
        {
            if (blocks[i] is not JsonObject block || block["type"]?.GetValueKind() != JsonValueKind.String)
            {
                error = $"Block {i}: must be an object with a 'type'.";
                return null;
            }

            var type = block["type"]!.GetValue<string>();
            var output = new JsonObject
            {
                ["id"] = String(block, "id") is { Length: > 0 and <= 64 } id ? id : Guid.NewGuid().ToString("N"),
                ["type"] = type,
            };

            error = type switch
            {
                "heading" => Heading(block, output, text),
                "paragraph" or "quote" => Text(block, output, "text", text),
                "callout" => Callout(block, output, text),
                "code" => Code(block, output, text),
                "bullet_list" or "numbered_list" => List(block, output, text),
                "checklist" => Checklist(block, output, text),
                "image" => Url(block, output, "url", required: true) ?? Optional(block, output, "alt", text),
                "file" => Url(block, output, "url", required: true) ?? Optional(block, output, "name", text),
                "link" => Url(block, output, "url", required: true) ?? Optional(block, output, "label", text),
                "task_reference" => Reference(block, output, "taskId", tasks),
                "project_reference" => Reference(block, output, "projectId", projects),
                "knowledge_reference" => Reference(block, output, "articleId", articles),
                "insight" => Insight(block, output, text),
                "data_table" => DataTable(block, output, text),
                _ => $"unknown type '{type}'",
            };

            if (error is not null)
            {
                error = $"Block {i} ({type}): {error}";
                return null;
            }

            normalized.Add(output);
        }

        return new Normalized(
            new JsonObject { ["blocks"] = normalized },
            text.ToString().Trim(),
            new BlockReferences(tasks.Distinct().ToList(), projects.Distinct().ToList(), articles.Distinct().ToList()));
    }

    /// <summary>Plain text of stored content, e.g. for search after a restore.</summary>
    public static string PlainText(string contentJson) =>
        Normalize(JsonNode.Parse(contentJson), out _)?.PlainText ?? string.Empty;

    private static string? Heading(JsonObject block, JsonObject output, StringBuilder text)
    {
        var level = block["level"] is JsonValue value && value.TryGetValue<int>(out var l) ? l : 2;
        if (level is < 1 or > 3)
        {
            return "level must be 1, 2 or 3";
        }

        output["level"] = level;
        return Text(block, output, "text", text);
    }

    private static string? Callout(JsonObject block, JsonObject output, StringBuilder text)
    {
        var tone = String(block, "tone") ?? "info";
        if (!CalloutTones.Contains(tone))
        {
            return $"tone must be one of {string.Join(", ", CalloutTones)}";
        }

        output["tone"] = tone;
        return Text(block, output, "text", text);
    }

    private static string? Code(JsonObject block, JsonObject output, StringBuilder text)
    {
        if (String(block, "language") is { } language)
        {
            if (language.Length > 40)
            {
                return "language is too long";
            }

            output["language"] = language;
        }

        return Text(block, output, "code", text);
    }

    private static string? List(JsonObject block, JsonObject output, StringBuilder text)
    {
        if (block["items"] is not JsonArray items || items.Count > MaxItems)
        {
            return $"items must be an array of at most {MaxItems} strings";
        }

        var result = new JsonArray();
        foreach (var item in items)
        {
            if (item?.GetValueKind() != JsonValueKind.String || item.GetValue<string>().Length > MaxTextLength)
            {
                return "items must be strings";
            }

            result.Add(item.GetValue<string>());
            text.AppendLine(item.GetValue<string>());
        }

        output["items"] = result;
        return null;
    }

    private static string? Checklist(JsonObject block, JsonObject output, StringBuilder text)
    {
        if (block["items"] is not JsonArray items || items.Count > MaxItems)
        {
            return $"items must be an array of at most {MaxItems} entries";
        }

        var result = new JsonArray();
        foreach (var item in items)
        {
            if (item is not JsonObject entry || String(entry, "text") is not { Length: <= MaxTextLength } itemText)
            {
                return "items must have a 'text'";
            }

            var done = entry["checked"] is JsonValue value && value.TryGetValue<bool>(out var c) && c;
            result.Add(new JsonObject { ["text"] = itemText, ["checked"] = done });
            text.AppendLine(itemText);
        }

        output["items"] = result;
        return null;
    }

    /// <summary>The card of an insight (ADR 0023): labelled text fields such as "Hypothese" or "Erkenntnis".</summary>
    private static string? Insight(JsonObject block, JsonObject output, StringBuilder text)
    {
        if (block["fields"] is not JsonArray fields || fields.Count > MaxInsightFields)
        {
            return $"fields must be an array of at most {MaxInsightFields} entries";
        }

        var result = new JsonArray();
        var keys = new HashSet<string>();
        foreach (var field in fields)
        {
            if (field is not JsonObject entry
                || String(entry, "key") is not { Length: > 0 and <= 32 } key || !key.All(c => c is >= 'a' and <= 'z' or '_')
                || String(entry, "label") is not { Length: > 0 and <= 100 } label
                || (String(entry, "text") ?? string.Empty) is not { Length: <= MaxTextLength } fieldText)
            {
                return "every field needs a key (a-z, _), a label of at most 100 and a text of at most 20000 characters";
            }

            if (!keys.Add(key))
            {
                return $"field '{key}' appears twice";
            }

            result.Add(new JsonObject { ["key"] = key, ["label"] = label, ["text"] = fieldText });
            if (fieldText.Length > 0)
            {
                text.Append(label).Append(": ").AppendLine(fieldText);
            }
        }

        output["fields"] = result;
        return null;
    }

    /// <summary>
    /// Measurements or any other table pasted from a spreadsheet: column names and rows of plain text cells. Only the title
    /// and the column names go into the search text, the cells would drown it.
    /// </summary>
    private static string? DataTable(JsonObject block, JsonObject output, StringBuilder text)
    {
        if (Optional(block, output, "title", text) is { } titleError)
        {
            return titleError;
        }

        if (block["columns"] is not JsonArray columns || columns.Count is 0 || columns.Count > MaxTableColumns
            || columns.Any(c => c?.GetValueKind() != JsonValueKind.String || c.GetValue<string>().Length > MaxCellLength))
        {
            return $"columns must be 1 to {MaxTableColumns} names of at most {MaxCellLength} characters";
        }

        if (block["rows"] is not JsonArray rows || rows.Count > MaxTableRows)
        {
            return $"rows must be an array of at most {MaxTableRows} rows";
        }

        var normalizedRows = new JsonArray();
        foreach (var row in rows)
        {
            if (row is not JsonArray cells || cells.Count != columns.Count
                || cells.Any(c => c?.GetValueKind() != JsonValueKind.String || c.GetValue<string>().Length > MaxCellLength))
            {
                return $"every row needs one text cell of at most {MaxCellLength} characters per column";
            }

            normalizedRows.Add(new JsonArray(cells.Select(c => (JsonNode)JsonValue.Create(c!.GetValue<string>())).ToArray()));
        }

        var chart = String(block, "chart") ?? "none";
        if (!ChartKinds.Contains(chart))
        {
            return $"chart must be one of {string.Join(", ", ChartKinds)}";
        }

        var names = columns.Select(c => c!.GetValue<string>()).ToList();
        output["columns"] = new JsonArray(names.Select(n => (JsonNode)JsonValue.Create(n)).ToArray());
        output["rows"] = normalizedRows;
        output["chart"] = chart;
        text.AppendLine(string.Join(" ", names));
        return null;
    }

    private static string? Text(JsonObject block, JsonObject output, string field, StringBuilder text)
    {
        var value = String(block, field) ?? string.Empty;
        if (value.Length > MaxTextLength)
        {
            return $"{field} is longer than {MaxTextLength} characters";
        }

        output[field] = value;
        text.AppendLine(value);
        return null;
    }

    private static string? Optional(JsonObject block, JsonObject output, string field, StringBuilder text)
    {
        if (String(block, field) is not { } value)
        {
            return null;
        }

        if (value.Length > 500)
        {
            return $"{field} is too long";
        }

        output[field] = value;
        text.AppendLine(value);
        return null;
    }

    /// <summary>Only http(s) and same-origin paths: no javascript: or data: URLs end up in content.</summary>
    private static string? Url(JsonObject block, JsonObject output, string field, bool required)
    {
        var value = String(block, field);
        if (value is null)
        {
            return required ? $"{field} is required" : null;
        }

        var allowed = value.Length <= 2000
                      && ((Uri.TryCreate(value, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp))
                          || (value.StartsWith('/') && !value.StartsWith("//", StringComparison.Ordinal)));
        if (!allowed)
        {
            return $"{field} must be an http(s) URL or a path starting with /";
        }

        output[field] = value;
        return null;
    }

    private static string? Reference(JsonObject block, JsonObject output, string field, List<Guid> ids)
    {
        if (!Guid.TryParse(String(block, field), out var id))
        {
            return $"{field} must be an id";
        }

        output[field] = id.ToString();
        ids.Add(id);
        return null;
    }

    private static string? String(JsonObject block, string field) =>
        block[field] is JsonValue value && value.GetValueKind() == JsonValueKind.String ? value.GetValue<string>() : null;
}
