using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using ProjectHub.Api.Modules.Knowledge;

namespace ProjectHub.Api.Modules.Ai;

/// <summary>
/// Turns the simple Markdown a language model writes into article blocks (<see cref="BlockContent"/>) and back:
/// headings, paragraphs, lists, checklists, quotes and code. Inline formatting stays as typed, because blocks hold plain text.
/// </summary>
public static partial class MarkdownBlocks
{
    public static JsonObject ToContent(string markdown)
    {
        var blocks = new JsonArray();
        var paragraph = new List<string>();
        var lines = markdown.Replace("\r\n", "\n").Split('\n');

        void FlushParagraph()
        {
            if (paragraph.Count > 0)
            {
                blocks.Add(new JsonObject { ["type"] = "paragraph", ["text"] = string.Join("\n", paragraph) });
                paragraph.Clear();
            }
        }

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var trimmed = line.Trim();

            if (trimmed.StartsWith("```", StringComparison.Ordinal))
            {
                FlushParagraph();
                var language = trimmed[3..].Trim();
                var code = new List<string>();
                while (++i < lines.Length && !lines[i].Trim().StartsWith("```", StringComparison.Ordinal))
                {
                    code.Add(lines[i]);
                }

                var block = new JsonObject { ["type"] = "code", ["code"] = string.Join("\n", code) };
                if (language.Length is > 0 and <= 40)
                {
                    block["language"] = language;
                }

                blocks.Add(block);
                continue;
            }

            if (trimmed.Length == 0)
            {
                FlushParagraph();
                continue;
            }

            if (Heading().Match(trimmed) is { Success: true } heading)
            {
                FlushParagraph();
                blocks.Add(new JsonObject
                {
                    ["type"] = "heading",
                    ["level"] = Math.Min(heading.Groups[1].Length, 3),
                    ["text"] = heading.Groups[2].Value.Trim(),
                });
                continue;
            }

            if (Checklist().IsMatch(trimmed))
            {
                FlushParagraph();
                var items = new JsonArray();
                for (; i < lines.Length && Checklist().Match(lines[i].Trim()) is { Success: true } item; i++)
                {
                    items.Add(new JsonObject { ["text"] = item.Groups[2].Value.Trim(), ["checked"] = item.Groups[1].Value is "x" or "X" });
                }

                i--;
                blocks.Add(new JsonObject { ["type"] = "checklist", ["items"] = items });
                continue;
            }

            if (Bullet().IsMatch(trimmed) || Numbered().IsMatch(trimmed))
            {
                FlushParagraph();
                var numbered = Numbered().IsMatch(trimmed);
                var pattern = numbered ? Numbered() : Bullet();
                var items = new JsonArray();
                for (; i < lines.Length && pattern.Match(lines[i].Trim()) is { Success: true } item && !Checklist().IsMatch(lines[i].Trim()); i++)
                {
                    items.Add(item.Groups[1].Value.Trim());
                }

                i--;
                blocks.Add(new JsonObject { ["type"] = numbered ? "numbered_list" : "bullet_list", ["items"] = items });
                continue;
            }

            if (trimmed.StartsWith('>'))
            {
                FlushParagraph();
                var quote = new List<string>();
                for (; i < lines.Length && lines[i].Trim().StartsWith('>'); i++)
                {
                    quote.Add(lines[i].Trim()[1..].Trim());
                }

                i--;
                blocks.Add(new JsonObject { ["type"] = "quote", ["text"] = string.Join("\n", quote) });
                continue;
            }

            paragraph.Add(trimmed);
        }

        FlushParagraph();
        return new JsonObject { ["blocks"] = blocks };
    }

    /// <summary>
    /// New text for an article: the blocks from <paramref name="markdown"/>, followed by the links, files, images and
    /// references of <paramref name="current"/>, which Markdown from a model can not carry.
    /// </summary>
    public static JsonObject ReplaceText(JsonObject current, string markdown)
    {
        var content = ToContent(markdown);
        var blocks = content["blocks"]!.AsArray();
        foreach (var block in current["blocks"]?.AsArray() ?? [])
        {
            if (block is JsonObject b && !TextTypes.Contains(b["type"]?.GetValue<string>() ?? string.Empty))
            {
                blocks.Add(b.DeepClone());
            }
        }

        return content;
    }

    private static readonly HashSet<string> TextTypes =
        ["heading", "paragraph", "quote", "callout", "code", "bullet_list", "numbered_list", "checklist"];

    /// <summary>Plain Markdown of stored blocks, for showing a model what an article contains.</summary>
    public static string FromContent(JsonObject content)
    {
        var text = new StringBuilder();
        foreach (var block in content["blocks"]?.AsArray() ?? [])
        {
            if (block is not JsonObject b)
            {
                continue;
            }

            var type = b["type"]?.GetValue<string>();
            switch (type)
            {
                case "heading":
                    text.Append(new string('#', b["level"]?.GetValue<int>() ?? 2)).Append(' ').AppendLine(b["text"]?.GetValue<string>());
                    break;
                case "paragraph" or "callout":
                    text.AppendLine(b["text"]?.GetValue<string>());
                    break;
                case "quote":
                    foreach (var line in (b["text"]?.GetValue<string>() ?? string.Empty).Split('\n'))
                    {
                        text.Append("> ").AppendLine(line);
                    }

                    break;
                case "code":
                    text.Append("```").AppendLine(b["language"]?.GetValue<string>()).AppendLine(b["code"]?.GetValue<string>()).AppendLine("```");
                    break;
                case "bullet_list" or "numbered_list":
                    var number = 1;
                    foreach (var item in b["items"]?.AsArray() ?? [])
                    {
                        text.Append(type == "numbered_list" ? $"{number++}. " : "- ").AppendLine(item?.GetValue<string>());
                    }

                    break;
                case "checklist":
                    foreach (var item in b["items"]?.AsArray() ?? [])
                    {
                        text.Append(item?["checked"]?.GetValue<bool>() == true ? "- [x] " : "- [ ] ").AppendLine(item?["text"]?.GetValue<string>());
                    }

                    break;
                default:
                    // Links, files, images and references are not text; ReplaceText keeps them.
                    continue;
            }

            text.AppendLine();
        }

        return text.ToString().TrimEnd();
    }

    [GeneratedRegex(@"^(#{1,6})\s+(.+)$")]
    private static partial Regex Heading();

    [GeneratedRegex(@"^[-*+]\s+\[([ xX])\]\s+(.*)$")]
    private static partial Regex Checklist();

    [GeneratedRegex(@"^[-*+]\s+(.+)$")]
    private static partial Regex Bullet();

    [GeneratedRegex(@"^\d+[.)]\s+(.+)$")]
    private static partial Regex Numbered();
}
