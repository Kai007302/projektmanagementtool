using ProjectHub.Api.Modules.Tasks;
using TaskStatus = ProjectHub.Api.Modules.Tasks.TaskStatus;

namespace ProjectHub.Api.UnitTests;

public sealed class TaskProgressTests
{
    [Theory]
    [InlineData(TaskStatus.Todo, 0)]
    [InlineData(TaskStatus.InProgress, 50)]
    [InlineData(TaskStatus.Done, 100)]
    public void A_task_without_subtasks_counts_by_its_status(string status, short expected) =>
        Assert.Equal(expected, TaskProgress.Calculate([new TaskProgress.Node(Guid.NewGuid(), null, status)]).Single().Value);

    [Fact]
    public void A_task_with_subtasks_is_the_rounded_mean_of_them_down_the_whole_tree()
    {
        var phase = Node(null, TaskStatus.Todo);
        var design = Node(phase.Id, TaskStatus.InProgress);
        var sketch = Node(design.Id, TaskStatus.Done);
        var review = Node(design.Id, TaskStatus.InProgress);
        var texts = Node(phase.Id, TaskStatus.Todo);
        var images = Node(phase.Id, TaskStatus.InProgress);

        var progress = TaskProgress.Calculate([texts, review, phase, images, sketch, design]);

        Assert.Equal(75, progress[design.Id]); // (100 + 50) / 2
        Assert.Equal(42, progress[phase.Id]); // (75 + 0 + 50) / 3 = 41.67
    }

    [Fact]
    public void A_done_task_is_complete_whatever_its_subtasks_say()
    {
        var parent = Node(null, TaskStatus.Done);
        var child = Node(parent.Id, TaskStatus.Todo);

        var progress = TaskProgress.Calculate([parent, child]);

        Assert.Equal((100, 0), (progress[parent.Id], progress[child.Id]));
    }

    [Fact]
    public void Corrupt_cycles_do_not_hang()
    {
        var first = new TaskProgress.Node(Guid.NewGuid(), null, TaskStatus.Todo);
        var second = new TaskProgress.Node(Guid.NewGuid(), first.Id, TaskStatus.Done);
        first = first with { ParentTaskId = second.Id };

        var progress = TaskProgress.Calculate([first, second]);

        Assert.Equal(2, progress.Count);
    }

    private static TaskProgress.Node Node(Guid? parentId, string status) => new(Guid.NewGuid(), parentId, status);
}
