using ProjectHub.Api.Modules.Gantt;

namespace ProjectHub.Api.UnitTests;

public sealed class GanttRulesTests
{
    private static readonly DateOnly Day = new(2026, 11, 2);

    private static (DateOnly, DateOnly)? Span(int start, int end) => (Day.AddDays(start), Day.AddDays(end));

    [Fact]
    public void A_task_with_one_date_takes_a_single_day()
    {
        Assert.Equal((Day, Day), GanttRules.Span(Day, null));
        Assert.Equal((Day, Day), GanttRules.Span(null, Day));
        Assert.Null(GanttRules.Span(null, null));
    }

    [Theory]
    [InlineData(DependencyTypes.FinishToStart, 0, 4, 5, 6, false)]
    [InlineData(DependencyTypes.FinishToStart, 0, 4, 4, 6, true)]
    [InlineData(DependencyTypes.StartToStart, 2, 4, 2, 3, false)]
    [InlineData(DependencyTypes.StartToStart, 2, 4, 1, 3, true)]
    [InlineData(DependencyTypes.FinishToFinish, 0, 4, 1, 4, false)]
    [InlineData(DependencyTypes.FinishToFinish, 0, 4, 1, 3, true)]
    [InlineData(DependencyTypes.StartToFinish, 3, 5, 0, 3, false)]
    [InlineData(DependencyTypes.StartToFinish, 3, 5, 0, 2, true)]
    public void Dependencies_are_checked_on_inclusive_days(string type, int sourceStart, int sourceEnd, int targetStart, int targetEnd, bool violated) =>
        Assert.Equal(violated, GanttRules.IsViolated(type, Span(sourceStart, sourceEnd), Span(targetStart, targetEnd)));

    [Fact]
    public void Missing_dates_never_violate() =>
        Assert.False(GanttRules.IsViolated(DependencyTypes.FinishToStart, Span(0, 4), null));

    [Fact]
    public void Cycles_are_found_over_several_steps()
    {
        Guid a = Guid.NewGuid(), b = Guid.NewGuid(), c = Guid.NewGuid(), d = Guid.NewGuid();
        (Guid, Guid)[] edges = [(a, b), (b, c)];

        Assert.True(GanttRules.WouldCreateCycle(edges, c, a));
        Assert.False(GanttRules.WouldCreateCycle(edges, a, c));
        Assert.False(GanttRules.WouldCreateCycle(edges, d, a));
    }

    [Fact]
    public void Ancestors_are_found_up_the_hierarchy()
    {
        Guid root = Guid.NewGuid(), child = Guid.NewGuid(), grandchild = Guid.NewGuid(), other = Guid.NewGuid();
        var parents = new Dictionary<Guid, Guid?> { [root] = null, [child] = root, [grandchild] = child, [other] = null };

        Assert.True(GanttRules.IsAncestor(parents, root, grandchild));
        Assert.False(GanttRules.IsAncestor(parents, grandchild, root));
        Assert.False(GanttRules.IsAncestor(parents, other, grandchild));
    }
}
