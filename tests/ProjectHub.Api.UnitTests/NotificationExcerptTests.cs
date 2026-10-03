using ProjectHub.Api.Modules.Notifications;

namespace ProjectHub.Api.UnitTests;

public sealed class NotificationExcerptTests
{
    [Fact]
    public void Excerpts_collapse_whitespace_and_are_shortened()
    {
        Assert.Null(NotificationDispatcher.Excerpt("  \n "));
        Assert.Equal("Bitte prüfen: Farben", NotificationDispatcher.Excerpt("Bitte prüfen:\n\n  Farben "));

        var excerpt = NotificationDispatcher.Excerpt(new string('a', 500))!;
        Assert.Equal(NotificationDispatcher.MaxBodyLength, excerpt.Length);
        Assert.EndsWith("…", excerpt);
    }
}
