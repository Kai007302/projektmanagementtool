using System.Text.Json.Nodes;
using ProjectHub.Api.Modules.Knowledge;

namespace ProjectHub.Api.UnitTests;

public class BlockContentTests
{
    [Fact]
    public void Normalizes_known_blocks_and_collects_text_and_references()
    {
        var taskId = Guid.NewGuid();
        var content = JsonNode.Parse($$"""
            {"blocks":[
              {"type":"heading","text":"Titel","extra":"weg"},
              {"type":"checklist","items":[{"text":"Eins","checked":true},{"text":"Zwei"}]},
              {"type":"callout","text":"Hinweis"},
              {"type":"task_reference","taskId":"{{taskId}}"},
              {"type":"link","url":"/projects","label":"Projekte"}
            ]}
            """);

        var normalized = BlockContent.Normalize(content, out var error);

        Assert.Null(error);
        var blocks = normalized!.Content["blocks"]!.AsArray();
        Assert.Equal(2, blocks[0]!["level"]!.GetValue<int>());
        Assert.Null(blocks[0]!["extra"]);
        Assert.False(string.IsNullOrEmpty(blocks[0]!["id"]!.GetValue<string>()));
        Assert.False(blocks[1]!["items"]![1]!["checked"]!.GetValue<bool>());
        Assert.Equal("info", blocks[2]!["tone"]!.GetValue<string>());
        Assert.Equal([taskId], normalized.References.Tasks);
        Assert.Equal(["Titel", "Eins", "Zwei", "Hinweis", "Projekte"], normalized.PlainText.Split(Environment.NewLine));
    }

    [Theory]
    [InlineData("""{"text":"kein blocks-Feld"}""")]
    [InlineData("""{"blocks":[{"type":"html","text":"<b>x</b>"}]}""")]
    [InlineData("""{"blocks":[{"type":"heading","level":4,"text":"x"}]}""")]
    [InlineData("""{"blocks":[{"type":"callout","tone":"danger","text":"x"}]}""")]
    [InlineData("""{"blocks":[{"type":"image","url":"javascript:alert(1)"}]}""")]
    [InlineData("""{"blocks":[{"type":"image","url":"data:image/png;base64,AAAA"}]}""")]
    [InlineData("""{"blocks":[{"type":"link","url":"//evil.example"}]}""")]
    [InlineData("""{"blocks":[{"type":"file"}]}""")]
    [InlineData("""{"blocks":[{"type":"bullet_list","items":[1,2]}]}""")]
    [InlineData("""{"blocks":[{"type":"project_reference","projectId":"nein"}]}""")]
    [InlineData("""{"blocks":["text"]}""")]
    [InlineData("""{"blocks":[{"type":"insight","fields":[{"key":"Frage!","label":"Frage","text":"x"}]}]}""")]
    [InlineData("""{"blocks":[{"type":"insight","fields":[{"key":"a","label":"A"},{"key":"a","label":"B"}]}]}""")]
    [InlineData("""{"blocks":[{"type":"data_table","columns":[],"rows":[]}]}""")]
    [InlineData("""{"blocks":[{"type":"data_table","columns":["h","MPa"],"rows":[["1"]]}]}""")]
    [InlineData("""{"blocks":[{"type":"data_table","columns":["h"],"rows":[[1]]}]}""")]
    [InlineData("""{"blocks":[{"type":"data_table","columns":["h"],"rows":[],"chart":"pie"}]}""")]
    public void Rejects_invalid_content(string json)
    {
        Assert.Null(BlockContent.Normalize(JsonNode.Parse(json), out var error));
        Assert.NotNull(error);
    }

    [Fact]
    public void Rejects_too_many_blocks()
    {
        var blocks = new JsonArray(Enumerable.Range(0, BlockContent.MaxBlocks + 1).Select(_ => (JsonNode)new JsonObject { ["type"] = "paragraph" }).ToArray());

        Assert.Null(BlockContent.Normalize(new JsonObject { ["blocks"] = blocks }, out _));
    }

    [Theory]
    [InlineData("Projekt-Kickoff durchführen", "projekt-kickoff-durchfuehren")]
    [InlineData("Größe & Übersicht", "groesse-uebersicht")]
    [InlineData("  Café  Crème! ", "cafe-creme")]
    [InlineData("???", "artikel")]
    public void Slugs_are_readable_ascii(string title, string expected) =>
        Assert.Equal(expected, KnowledgeArticleService.Slugify(title));

    [Fact]
    public void Slugs_are_limited_in_length() =>
        Assert.True(KnowledgeArticleService.Slugify(new string('a', 200)).Length <= 80);

    [Fact]
    public void Keeps_insight_fields_and_data_tables_with_their_text()
    {
        var content = JsonNode.Parse("""
            {"blocks":[
              {"type":"insight","fields":[{"key":"question","label":"Frage","text":"Härtet B bei Kälte?"},{"key":"insight","label":"Erkenntnis","text":""}]},
              {"type":"data_table","title":"Zugfestigkeit","columns":["Stunden","A","B"],"rows":[["0","2","1"],["24","17","15"]],"chart":"line"}
            ]}
            """);

        var normalized = BlockContent.Normalize(content, out var error);

        Assert.Null(error);
        var blocks = normalized!.Content["blocks"]!.AsArray();
        Assert.Equal("Erkenntnis", blocks[0]!["fields"]![1]!["label"]!.GetValue<string>());
        Assert.Equal("17", blocks[1]!["rows"]![1]![1]!.GetValue<string>());
        Assert.Equal("line", blocks[1]!["chart"]!.GetValue<string>());
        // Cells stay out of the search text, the title and the column names go in; empty fields add nothing.
        Assert.Equal(["Frage: Härtet B bei Kälte?", "Zugfestigkeit", "Stunden A B"], normalized.PlainText.Split(Environment.NewLine));
    }

    [Fact]
    public void Limits_the_rows_of_a_data_table()
    {
        var rows = new JsonArray(Enumerable.Range(0, BlockContent.MaxTableRows + 1).Select(i => (JsonNode)new JsonArray(JsonValue.Create(i.ToString()))).ToArray());
        var content = new JsonObject { ["blocks"] = new JsonArray(new JsonObject { ["type"] = "data_table", ["columns"] = new JsonArray("n"), ["rows"] = rows }) };

        Assert.Null(BlockContent.Normalize(content, out _));
    }

    [Fact]
    public void Every_insight_kind_has_fields_one_highlight_and_known_tones()
    {
        var kinds = KnowledgeUseCases.All.SelectMany(u => u.Kinds).ToList();

        Assert.NotEmpty(kinds);
        Assert.All(kinds, kind =>
        {
            Assert.Contains(kind.ArticleType, KnowledgeArticleType.All);
            Assert.Single(kind.Fields, f => f.Highlight);
            Assert.Equal(kind.Fields.Count, kind.Fields.Select(f => f.Key).Distinct().Count());
            Assert.All(kind.Outcomes, o => Assert.Contains(o.Tone, KnowledgeUseCases.Tones));
            Assert.NotNull(BlockContent.Normalize(new JsonObject { ["blocks"] = new JsonArray(KnowledgeUseCases.InsightBlock(kind)) }, out _));
        });
        Assert.Equal(kinds.Count, kinds.Select(k => k.ArticleType).Distinct().Count());
    }
}
