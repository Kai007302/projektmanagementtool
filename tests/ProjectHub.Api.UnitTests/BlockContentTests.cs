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
}
