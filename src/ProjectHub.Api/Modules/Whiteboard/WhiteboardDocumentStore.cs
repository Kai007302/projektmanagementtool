using Microsoft.EntityFrameworkCore;
using ProjectHub.Api.Infrastructure.Database;
using ProjectHub.Api.Modules.Tasks;

namespace ProjectHub.Api.Modules.Whiteboard;

/// <summary>
/// Durable storage of whiteboard documents (ADR 0009): accepted updates go to <c>whiteboard_update</c>,
/// compaction merges them into a snapshot in <see cref="IWhiteboardSnapshotStorage"/>. All writers of one
/// whiteboard serialize on a PostgreSQL advisory lock, so instances need no shared memory.
/// Callers check permissions; this class only stores.
/// </summary>
public sealed class WhiteboardDocumentStore(
    ProjectHubDbContext db,
    IWhiteboardSnapshotStorage storage,
    WhiteboardEngine engine,
    TimeProvider clock,
    ILogger<WhiteboardDocumentStore> logger)
{
    /// <summary>Snapshots kept per whiteboard; older ones are deleted after compaction.</summary>
    public const int SnapshotsToKeep = 2;

    /// <summary>Poison updates removed in one go before giving up on a whiteboard.</summary>
    private const int MaxPoisonUpdates = 5;

    /// <summary>Stores an update that was already validated and returns its sequence number.</summary>
    public async Task<long> AppendAsync(ProjectWhiteboard board, Guid userId, byte[] update, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await LockAsync(board.Id, ct);

        var lastUpdate = await db.Set<WhiteboardUpdate>().Where(u => u.WhiteboardId == board.Id).MaxAsync(u => (long?)u.SequenceNumber, ct);
        var lastSnapshot = await db.Set<WhiteboardSnapshot>().Where(s => s.WhiteboardId == board.Id).MaxAsync(s => (long?)s.SequenceNumber, ct);
        var sequence = Math.Max(lastUpdate ?? 0, lastSnapshot ?? 0) + 1;

        db.Set<WhiteboardUpdate>().Add(new WhiteboardUpdate
        {
            WhiteboardId = board.Id,
            SequenceNumber = sequence,
            OrganizationId = board.OrganizationId,
            Payload = update,
            CreatedBy = userId,
            CreatedAt = clock.GetUtcNow(),
        });
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        db.ChangeTracker.Clear();
        return sequence;
    }

    /// <summary>The whole document as one Yjs update: latest snapshot plus all later updates.</summary>
    public async Task<byte[]> LoadAsync(Guid whiteboardId, CancellationToken ct)
    {
        // The lock keeps compaction from deleting updates or snapshot files between the reads below.
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await LockAsync(whiteboardId, ct);
        var (snapshot, updates) = await ReadPendingAsync(whiteboardId, ct);
        var snapshotBytes = snapshot is null ? null : await ReadSnapshotAsync(snapshot, ct);
        var state = await MergeAsync(snapshotBytes, updates, ct);
        await transaction.CommitAsync(ct);
        return state;
    }

    /// <summary>
    /// Merges pending updates into a new snapshot, deletes them and refreshes the task references.
    /// Returns false when there was nothing to do or another instance is compacting the same whiteboard.
    /// </summary>
    public async Task<bool> CompactAsync(Guid whiteboardId, CancellationToken ct)
    {
        var obsoleteKeys = new List<string>();
        string? newKey = null;
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            var locked = await db.Database
                .SqlQuery<bool>($"select pg_try_advisory_xact_lock(hashtextextended({LockName(whiteboardId)}, 0)) as \"Value\"")
                .SingleAsync(ct);
            var board = await db.Set<ProjectWhiteboard>().AsNoTracking().SingleOrDefaultAsync(w => w.Id == whiteboardId, ct);
            if (!locked || board is null)
            {
                return false;
            }

            var (snapshot, updates) = await ReadPendingAsync(whiteboardId, ct);
            if (updates.Count == 0)
            {
                return false;
            }

            var snapshotBytes = snapshot is null ? null : await ReadSnapshotAsync(snapshot, ct);
            var state = await MergeAsync(snapshotBytes, updates, ct);
            var sequence = updates[^1].SequenceNumber;

            newKey = $"{board.OrganizationId:N}/{board.Id:N}/{sequence:D12}-{Guid.NewGuid():N}.ybin";
            await storage.SaveAsync(newKey, state, ct);
            db.Set<WhiteboardSnapshot>().Add(new WhiteboardSnapshot
            {
                Id = Guid.CreateVersion7(),
                OrganizationId = board.OrganizationId,
                WhiteboardId = board.Id,
                StorageKey = newKey,
                SequenceNumber = sequence,
                CreatedAt = clock.GetUtcNow(),
            });
            await db.Set<WhiteboardUpdate>()
                .Where(u => u.WhiteboardId == board.Id && u.SequenceNumber <= sequence)
                .ExecuteDeleteAsync(ct);

            var obsolete = await db.Set<WhiteboardSnapshot>().AsTracking()
                .Where(s => s.WhiteboardId == board.Id)
                .OrderByDescending(s => s.SequenceNumber)
                .Skip(SnapshotsToKeep - 1)
                .ToListAsync(ct);
            db.Set<WhiteboardSnapshot>().RemoveRange(obsolete);
            obsoleteKeys.AddRange(obsolete.Select(s => s.StorageKey));

            await SyncReferencesAsync(board, await engine.ReadTaskCardsAsync(state, ct), ct);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            newKey = null;
        }
        finally
        {
            db.ChangeTracker.Clear();
            if (newKey is not null)
            {
                // The snapshot row was never committed; its file must not linger.
                await DeleteFilesAsync([newKey]);
            }
        }

        await DeleteFilesAsync(obsoleteKeys);
        return true;
    }

    /// <summary>Whiteboards with pending updates that are quiet or have many updates waiting.</summary>
    public async Task<IReadOnlyList<Guid>> DueForCompactionAsync(TimeSpan quietPeriod, int maxPendingUpdates, int limit, CancellationToken ct)
    {
        var quietSince = clock.GetUtcNow() - quietPeriod;
        return await db.Set<WhiteboardUpdate>()
            .GroupBy(u => u.WhiteboardId)
            .Where(g => g.Max(u => u.CreatedAt) <= quietSince || g.Count() >= maxPendingUpdates)
            .OrderBy(g => g.Min(u => u.CreatedAt))
            .Select(g => g.Key)
            .Take(limit)
            .ToListAsync(ct);
    }

    /// <summary>Takes the whiteboard's lock in the caller's transaction (used when deleting a whiteboard).</summary>
    public Task LockAsync(Guid whiteboardId, CancellationToken ct) =>
        db.Database.ExecuteSqlInterpolatedAsync($"select pg_advisory_xact_lock(hashtextextended({LockName(whiteboardId)}, 0))", ct);

    /// <summary>Storage keys of all snapshots, to delete their files after the whiteboard itself is gone.</summary>
    public async Task<IReadOnlyList<string>> SnapshotKeysAsync(Guid whiteboardId, CancellationToken ct) =>
        await db.Set<WhiteboardSnapshot>().Where(s => s.WhiteboardId == whiteboardId).Select(s => s.StorageKey).ToListAsync(ct);

    public async Task DeleteFilesAsync(IEnumerable<string> keys)
    {
        foreach (var key in keys)
        {
            try
            {
                await storage.DeleteAsync(key, CancellationToken.None);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Could not delete whiteboard snapshot {StorageKey}", key);
            }
        }
    }

    /// <summary>
    /// Merges in the engine. If the engine fails, some stored update breaks the document when combined with
    /// the others (each one passed validation alone). The first such update is found by bisecting, deleted
    /// and logged, so one bad update cannot make a whiteboard unloadable. Runs under the whiteboard's lock.
    /// </summary>
    private async Task<byte[]> MergeAsync(byte[]? snapshot, List<WhiteboardUpdate> updates, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                return await engine.MergeAsync(snapshot, updates.Select(u => u.Payload).ToList(), ct);
            }
            catch (WhiteboardEngineException) when (attempt < MaxPoisonUpdates && updates.Count > 0)
            {
                var poison = await FindFirstFailingAsync(snapshot, updates, ct);
                logger.LogWarning(
                    "Removed update {Sequence} of whiteboard {WhiteboardId} by {UserId}: it breaks the document",
                    poison.SequenceNumber, poison.WhiteboardId, poison.CreatedBy);
                await db.Set<WhiteboardUpdate>()
                    .Where(u => u.WhiteboardId == poison.WhiteboardId && u.SequenceNumber == poison.SequenceNumber)
                    .ExecuteDeleteAsync(ct);
                updates.Remove(poison);
            }
        }
    }

    /// <summary>The update whose prefix first fails to merge; the snapshot alone was produced by the engine and merges.</summary>
    private async Task<WhiteboardUpdate> FindFirstFailingAsync(byte[]? snapshot, List<WhiteboardUpdate> updates, CancellationToken ct)
    {
        int good = 0, bad = updates.Count;
        while (bad - good > 1)
        {
            var middle = (good + bad) / 2;
            try
            {
                await engine.MergeAsync(snapshot, updates.Take(middle).Select(u => u.Payload).ToList(), ct);
                good = middle;
            }
            catch (WhiteboardEngineException)
            {
                bad = middle;
            }
        }

        return updates[bad - 1];
    }

    private static string LockName(Guid whiteboardId) => "whiteboard:" + whiteboardId;

    private async Task<(WhiteboardSnapshot? Snapshot, List<WhiteboardUpdate> Updates)> ReadPendingAsync(Guid whiteboardId, CancellationToken ct)
    {
        var snapshot = await db.Set<WhiteboardSnapshot>().AsNoTracking()
            .Where(s => s.WhiteboardId == whiteboardId)
            .OrderByDescending(s => s.SequenceNumber)
            .FirstOrDefaultAsync(ct);
        var after = snapshot?.SequenceNumber ?? 0;
        var updates = await db.Set<WhiteboardUpdate>().AsNoTracking()
            .Where(u => u.WhiteboardId == whiteboardId && u.SequenceNumber > after)
            .OrderBy(u => u.SequenceNumber)
            .ToListAsync(ct);
        return (snapshot, updates);
    }

    private async Task<byte[]> ReadSnapshotAsync(WhiteboardSnapshot snapshot, CancellationToken ct) =>
        await storage.ReadAsync(snapshot.StorageKey, ct)
        ?? throw new InvalidOperationException($"Snapshot {snapshot.StorageKey} of whiteboard {snapshot.WhiteboardId} is missing.");

    /// <summary>Makes <c>whiteboard_reference</c> match the task cards on the board that point to active tasks of its project.</summary>
    private async Task SyncReferencesAsync(ProjectWhiteboard board, IReadOnlyList<TaskCard> cards, CancellationToken ct)
    {
        var taskIds = cards.Select(c => c.TaskId).Distinct().ToList();
        var valid = (await db.Set<ProjectTask>()
                .Where(t => taskIds.Contains(t.Id) && t.OrganizationId == board.OrganizationId && t.ProjectId == board.ProjectId && t.DeletedAt == null)
                .Select(t => t.Id)
                .ToListAsync(ct))
            .ToHashSet();
        var wanted = cards.Where(c => valid.Contains(c.TaskId)).DistinctBy(c => c.ObjectId).ToList();

        var existing = await db.Set<WhiteboardReference>().AsNoTracking().Where(r => r.WhiteboardId == board.Id).ToListAsync(ct);
        var stale = existing.Where(r => !wanted.Contains(new TaskCard(r.ObjectId, r.TaskId))).Select(r => r.Id).ToList();
        if (stale.Count > 0)
        {
            // Deleted right away, so a card that now points to another task can be re-added under the same object id.
            await db.Set<WhiteboardReference>().Where(r => stale.Contains(r.Id)).ExecuteDeleteAsync(ct);
        }

        var kept = existing.Where(r => !stale.Contains(r.Id)).Select(r => r.ObjectId).ToHashSet();
        var now = clock.GetUtcNow();
        db.Set<WhiteboardReference>().AddRange(wanted.Where(c => !kept.Contains(c.ObjectId)).Select(c => new WhiteboardReference
        {
            Id = Guid.CreateVersion7(),
            OrganizationId = board.OrganizationId,
            WhiteboardId = board.Id,
            TaskId = c.TaskId,
            ObjectId = c.ObjectId,
            CreatedAt = now,
        }));
    }
}
