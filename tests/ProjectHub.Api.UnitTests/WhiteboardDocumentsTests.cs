using ProjectHub.Api.Modules.Whiteboard;

namespace ProjectHub.Api.UnitTests;

public sealed class WhiteboardDocumentsTests
{
    private static readonly Guid TaskId = Guid.Parse("01920000-0000-7000-8000-000000000503");

    [Fact]
    public void Updates_merge_into_one_state_with_all_objects()
    {
        var first = Update("a", new() { ["type"] = "sticky", ["text"] = "Eins", ["x"] = 1 });
        var second = Update("b", new() { ["type"] = "rect", ["x"] = 2.5 });

        var state = WhiteboardDocuments.Merge(null, [first, second]);
        var merged = WhiteboardDocuments.Merge(state, [Update("c", new() { ["type"] = "text" })]);

        var objects = WhiteboardDocuments.ReadObjects(merged);
        Assert.Equal(["a", "b", "c"], objects.Keys.Order());
        Assert.Equal("Eins", objects["a"]["text"]);
        Assert.Equal(2.5, objects["b"]["x"]);
    }

    [Fact]
    public void Applying_the_same_update_twice_changes_nothing()
    {
        var update = Update("a", new() { ["type"] = "sticky" });

        Assert.Equal(WhiteboardDocuments.ReadObjects(WhiteboardDocuments.Merge(null, [update])).Keys,
            WhiteboardDocuments.ReadObjects(WhiteboardDocuments.Merge(null, [update, update])).Keys);
    }

    [Fact]
    public void Only_well_formed_task_cards_are_read()
    {
        var state = WhiteboardDocuments.Merge(null, [
            Update("card", new() { ["type"] = "task", ["taskId"] = TaskId.ToString() }),
            Update("broken", new() { ["type"] = "task", ["taskId"] = "kein-guid" }),
            Update("note", new() { ["type"] = "sticky", ["taskId"] = TaskId.ToString() }),
            Update("numeric", new() { ["type"] = "task", ["taskId"] = 42 }),
        ]);

        Assert.Equal([new TaskCard("card", TaskId)], WhiteboardDocuments.ReadTaskCards(state));
    }

    [Fact]
    public void Garbage_is_not_a_valid_update()
    {
        Assert.True(WhiteboardDocuments.IsValidUpdate(Update("a", new() { ["type"] = "sticky" })));
        Assert.False(WhiteboardDocuments.IsValidUpdate([]));
        Assert.False(WhiteboardDocuments.IsValidUpdate([1, 2, 3, 99, 7]));
    }

    [Theory]
    [MemberData(nameof(BrowserUpdates))]
    public void Updates_from_the_browser_are_well_formed(string hex) =>
        Assert.True(YjsUpdateValidator.IsWellFormed(Convert.FromHexString(hex)));

    [Fact]
    public void Updates_from_the_server_are_well_formed()
    {
        var first = Update("a", new() { ["type"] = "task", ["taskId"] = TaskId.ToString(), ["x"] = 1.5 });
        Assert.True(YjsUpdateValidator.IsWellFormed(first));
        Assert.True(YjsUpdateValidator.IsWellFormed(WhiteboardDocuments.Merge(null, [first, Update("a", new() { ["type"] = "rect" }, first)])));
    }

    [Fact]
    public void Random_bytes_are_rejected_before_they_reach_native_code()
    {
        // yrs aborts the process on some malformed input; random bytes must never get that far.
        var random = new Random(4711);
        for (var i = 0; i < 20_000; i++)
        {
            var bytes = new byte[random.Next(1, 200)];
            random.NextBytes(bytes);
            Assert.False(YjsUpdateValidator.IsWellFormed(bytes), Convert.ToHexString(bytes));
        }
    }

    [Theory]
    [InlineData("0101A1B2C3040000")] // a struct of length 0 (info byte 0 = GC with length 0)
    [InlineData("01010500")] // client with a struct but truncated
    [InlineData("000101FFFFFFFFFFFFFFFFFF01")] // delete set with an oversized varint
    public void Malformed_updates_are_rejected(string hex) =>
        Assert.False(YjsUpdateValidator.IsWellFormed(Convert.FromHexString(hex)));

    [Fact]
    public void Only_the_objects_map_and_plain_values_are_accepted()
    {
        var text = Convert.FromHexString(TextUpdate);
        Assert.False(YjsUpdateValidator.IsWellFormed(text));
    }

    // Y.Text in a root called "objects", which whiteboards never use.
    private const string TextUpdate = "0101F698F4AD0C000401076F626A6563747302686900";

    public static TheoryData<string> BrowserUpdates()
    {
        var data = new TheoryData<string>();
        foreach (var line in File.ReadLines(Path.Combine(AppContext.BaseDirectory, "TestData", "yjs-browser-updates.txt")))
        {
            if (!line.StartsWith('#'))
            {
                data.Add(line);
            }
        }

        return data;
    }

    private static byte[] Update(string id, Dictionary<string, object> fields, byte[]? basedOn = null) =>
        WhiteboardDocuments.CreateUpdate(new Dictionary<string, IReadOnlyDictionary<string, object>> { [id] = fields }, basedOn);
}
