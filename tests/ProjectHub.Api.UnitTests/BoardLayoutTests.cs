using ProjectHub.Api.Modules.Kanban;

namespace ProjectHub.Api.UnitTests;

public class BoardLayoutTests
{
    private static readonly ColumnSlot Todo = new(Guid.NewGuid(), "todo");
    private static readonly ColumnSlot Doing = new(Guid.NewGuid(), "in_progress");
    private static readonly ColumnSlot Review = new(Guid.NewGuid(), "in_progress");
    private static readonly ColumnSlot Done = new(Guid.NewGuid(), "done");
    private static readonly ColumnSlot[] Columns = [Todo, Doing, Review, Done];

    private static readonly DateTimeOffset Start = new(2026, 10, 1, 8, 0, 0, TimeSpan.Zero);

    private static CardPlacement Card(string status, Guid? column = null, decimal? position = null, int minutes = 0) =>
        new(Guid.NewGuid(), status, column, position, Start.AddMinutes(minutes));

    [Fact]
    public void Unplaced_cards_go_to_the_first_column_of_their_status()
    {
        var card = Card("in_progress");

        var layout = BoardLayout.Arrange(Columns, [card]);

        Assert.Equal([card], layout[Doing.Id]);
        Assert.Empty(layout[Review.Id]);
    }

    [Fact]
    public void Placed_cards_stay_in_their_column_while_the_status_matches()
    {
        var card = Card("in_progress", Review.Id, 1);

        Assert.Equal([card], BoardLayout.Arrange(Columns, [card])[Review.Id]);
    }

    [Fact]
    public void A_status_change_elsewhere_moves_the_card_to_its_new_status_column()
    {
        var card = Card("done", Review.Id, 1);

        var layout = BoardLayout.Arrange(Columns, [card]);

        Assert.Equal([card], layout[Done.Id]);
        Assert.Equal(Done.Id, BoardLayout.ColumnOf(layout, card.TaskId));
    }

    [Fact]
    public void Cards_are_ordered_by_position_then_unplaced_ones_by_creation()
    {
        var late = Card("todo", minutes: 2);
        var early = Card("todo", minutes: 1);
        var second = Card("todo", Todo.Id, 2, minutes: 5);
        var first = Card("todo", Todo.Id, 1, minutes: 9);

        var layout = BoardLayout.Arrange(Columns, [late, second, early, first]);

        Assert.Equal([first, second, early, late], layout[Todo.Id]);
    }

    [Fact]
    public void Position_from_a_column_of_another_status_is_ignored()
    {
        var stale = Card("todo", Done.Id, 1, minutes: 9);
        var placed = Card("todo", Todo.Id, 5, minutes: 1);

        Assert.Equal([placed, stale], BoardLayout.Arrange(Columns, [stale, placed])[Todo.Id]);
    }

    [Fact]
    public void Unknown_status_falls_back_to_the_first_column()
    {
        var card = Card("archived");

        Assert.Equal([card], BoardLayout.Arrange(Columns, [card])[Todo.Id]);
    }
}
