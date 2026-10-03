namespace ProjectHub.Api.Infrastructure.Http;

public sealed record PagedResponse<T>(IReadOnlyList<T> Items, int? NextOffset);

/// <summary>Offset paging with a hard upper bound; list endpoints are never unbounded.</summary>
public readonly record struct Paging(int Skip, int Take)
{
    public const int DefaultPageSize = 50;
    public const int MaxPageSize = 100;

    public static bool TryCreate(int? limit, int? offset, out Paging paging, out IResult? error)
    {
        paging = new Paging(offset ?? 0, limit ?? DefaultPageSize);
        error = paging.Take is < 1 or > MaxPageSize
            ? ApiResults.Validation("limit", $"Must be between 1 and {MaxPageSize}.")
            : paging.Skip < 0
                ? ApiResults.Validation("offset", "Must not be negative.")
                : null;
        return error is null;
    }

    /// <summary>Builds the page from a query result fetched with <c>Take + 1</c> rows.</summary>
    public PagedResponse<T> ToPage<T>(IReadOnlyList<T> rowsPlusOne) =>
        new(rowsPlusOne.Take(Take).ToList(), rowsPlusOne.Count > Take ? Skip + Take : null);
}
