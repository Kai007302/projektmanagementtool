using YDotNet.Document;
using YDotNet.Document.Cells;
using YDotNet.Document.Options;
using YDotNet.Document.Transactions;

namespace ProjectHub.Api.Modules.Whiteboard;

/// <summary>A task card in a whiteboard document: the Yjs object id and the task it points to.</summary>
public sealed record TaskCard(string ObjectId, Guid TaskId);

/// <summary>
/// Server-side access to Yjs documents through YDotNet (bindings to yrs). Documents only live for the
/// duration of one call; the API never keeps them in memory (ADR 0009).
/// </summary>
public static class WhiteboardDocuments
{
    /// <summary>Name of the root map holding all whiteboard objects, keyed by object id.</summary>
    public const string ObjectsMap = "objects";

    public const string TaskType = "task";

    /// <summary>True when the bytes are a Yjs update (v1 encoding) that yrs can decode.</summary>
    public static bool IsValidUpdate(byte[] update)
    {
        if (update.Length == 0)
        {
            return false;
        }

        using var doc = new Doc();
        using var transaction = doc.WriteTransaction();
        var result = transaction.ApplyV1(update);
        transaction.Commit();
        return result == TransactionUpdateResult.Ok;
    }

    /// <summary>Merges a snapshot (may be null) and later updates into one update holding the whole document.</summary>
    public static byte[] Merge(byte[]? snapshot, IEnumerable<byte[]> updates)
    {
        using var doc = Load(snapshot, updates);
        using var transaction = doc.ReadTransaction();
        return transaction.StateDiffV1(null!);
    }

    /// <summary>All objects of type <c>task</c> with a well-formed task id. Malformed objects are ignored.</summary>
    public static IReadOnlyList<TaskCard> ReadTaskCards(byte[] state)
    {
        using var doc = new Doc();
        var objects = doc.Map(ObjectsMap);
        Apply(doc, state);

        var cards = new List<TaskCard>();
        using var transaction = doc.ReadTransaction();
        foreach (var entry in objects.Iterate(transaction))
        {
            if (entry.Value.Tag != OutputTag.Map)
            {
                continue;
            }

            var item = entry.Value.Map;
            if (StringOf(item.Get(transaction, "type")) == TaskType
                && Guid.TryParse(StringOf(item.Get(transaction, "taskId")), out var taskId))
            {
                cards.Add(new TaskCard(entry.Key, taskId));
            }
        }

        return cards;
    }

    /// <summary>
    /// Builds a Yjs update that adds the given objects (each a map of string or number fields).
    /// Used for development seed data and tests; browsers create their own updates.
    /// </summary>
    public static byte[] CreateUpdate(IReadOnlyDictionary<string, IReadOnlyDictionary<string, object>> objects, byte[]? basedOn = null)
    {
        // A writing replica needs a unique client id; YDotNet's default ids are small and collide.
        using var doc = new Doc(new DocOptions { Id = (ulong)Random.Shared.NextInt64(1, uint.MaxValue) });
        var map = doc.Map(ObjectsMap);
        if (basedOn is not null)
        {
            Apply(doc, basedOn);
        }

        byte[] before;
        using (var read = doc.ReadTransaction())
        {
            before = read.StateVectorV1();
        }

        using (var write = doc.WriteTransaction())
        {
            foreach (var (id, fields) in objects)
            {
                var inputs = fields.ToDictionary(f => f.Key, f => f.Value switch
                {
                    string text => Input.String(text),
                    double number => Input.Double(number),
                    int number => Input.Double(number),
                    _ => throw new ArgumentException($"Unsupported value for field {f.Key}."),
                });
                map.Insert(write, id, Input.Map(inputs));
            }

            write.Commit();
        }

        using var transaction = doc.ReadTransaction();
        return transaction.StateDiffV1(before);
    }

    /// <summary>Field values of all objects, for tests and diagnostics. Nested types are skipped.</summary>
    public static IReadOnlyDictionary<string, IReadOnlyDictionary<string, object?>> ReadObjects(byte[] state)
    {
        using var doc = new Doc();
        var objects = doc.Map(ObjectsMap);
        Apply(doc, state);

        var result = new Dictionary<string, IReadOnlyDictionary<string, object?>>();
        using var transaction = doc.ReadTransaction();
        foreach (var entry in objects.Iterate(transaction))
        {
            if (entry.Value.Tag != OutputTag.Map)
            {
                continue;
            }

            result[entry.Key] = entry.Value.Map.Iterate(transaction).ToDictionary(
                f => f.Key,
                f => f.Value.Tag switch
                {
                    OutputTag.String => f.Value.String,
                    OutputTag.Double => f.Value.Double,
                    OutputTag.Long => f.Value.Long,
                    OutputTag.Boolean => f.Value.Boolean,
                    _ => (object?)null,
                });
        }

        return result;
    }

    private static Doc Load(byte[]? snapshot, IEnumerable<byte[]> updates)
    {
        var doc = new Doc();
        if (snapshot is not null)
        {
            Apply(doc, snapshot);
        }

        foreach (var update in updates)
        {
            Apply(doc, update);
        }

        return doc;
    }

    private static void Apply(Doc doc, byte[] update)
    {
        using var transaction = doc.WriteTransaction();
        var result = transaction.ApplyV1(update);
        transaction.Commit();
        if (result != TransactionUpdateResult.Ok)
        {
            // Every stored update was validated before it was saved, so this means corrupted storage.
            throw new InvalidOperationException($"Stored whiteboard update could not be applied: {result}.");
        }
    }

    private static string? StringOf(Output? value) => value is { Tag: OutputTag.String } ? value.String : null;
}
