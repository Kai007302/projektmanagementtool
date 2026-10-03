using Microsoft.EntityFrameworkCore;
using ProjectHub.Api.Infrastructure.Outcomes;

namespace ProjectHub.Api.Infrastructure.Database;

/// <summary>A mutable entity guarded by optimistic concurrency on its <c>version</c> column.</summary>
public interface IVersioned
{
    long Version { get; set; }

    DateTimeOffset UpdatedAt { get; set; }
}

public static class Versioning
{
    /// <summary>
    /// Bumps the version. The UPDATE only matches if the row still has <paramref name="expectedVersion"/>,
    /// so a stale write can never overwrite a newer one.
    /// </summary>
    public static void Touch<T>(this DbContext db, T entity, long expectedVersion, DateTimeOffset now)
        where T : class, IVersioned
    {
        db.Entry(entity).Property(e => e.Version).OriginalValue = expectedVersion;
        entity.Version = expectedVersion + 1;
        entity.UpdatedAt = now;
    }

    /// <summary>Saves; a concurrent change becomes a conflict carrying the current version.</summary>
    public static async Task<ServiceFailure?> SaveVersionedAsync<T>(this DbContext db, T entity, CancellationToken ct)
        where T : class, IVersioned
    {
        try
        {
            await db.SaveChangesAsync(ct);
            return null;
        }
        catch (DbUpdateConcurrencyException)
        {
            var current = await db.Entry(entity).GetDatabaseValuesAsync(ct);
            db.ChangeTracker.Clear();
            return ServiceFailure.StaleVersion(current?.GetValue<long>(nameof(IVersioned.Version)) ?? 0);
        }
    }
}
