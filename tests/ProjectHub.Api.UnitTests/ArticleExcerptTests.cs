using ProjectHub.Api.Modules.Knowledge;

namespace ProjectHub.Api.UnitTests;

public sealed class ArticleExcerptTests
{
    [Fact]
    public void A_short_text_stays_whole() =>
        Assert.Equal("Wie wir deployen.", ArticleExcerpt.Of("  Wie wir\n deployen.  "));

    [Fact]
    public void A_long_text_is_cut_after_25_words()
    {
        var text = string.Join(' ', Enumerable.Range(1, 40).Select(i => $"Wort{i}"));

        var excerpt = ArticleExcerpt.Of(text + ".");

        Assert.Equal(string.Join(' ', Enumerable.Range(1, 25).Select(i => $"Wort{i}")) + " …", excerpt);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public void No_text_has_no_excerpt(string? text) => Assert.Null(ArticleExcerpt.Of(text));
}
