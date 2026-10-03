using ProjectHub.Api.Infrastructure.Outcomes;

namespace ProjectHub.Api.Infrastructure.Http;

/// <summary>Problem responses that never leak internals to the caller.</summary>
public static class ApiResults
{
    public static IResult NotFound(string resource) =>
        Results.Problem(statusCode: StatusCodes.Status404NotFound, title: $"{resource} not found.");

    public static IResult Forbidden(string detail) =>
        Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Forbidden", detail: detail);

    public static IResult Conflict(string detail) =>
        Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "Conflict", detail: detail);

    public static IResult Validation(string field, string message) =>
        Results.ValidationProblem(new Dictionary<string, string[]> { [field] = [message] });

    /// <summary>Maps a failed <see cref="ServiceResult{T}"/> to its problem response, or a success via <paramref name="onSuccess"/>.</summary>
    public static IResult From<T>(ServiceResult<T> result, Func<T, IResult> onSuccess) => result.Error switch
    {
        ServiceError.None => onSuccess(result.Value!),
        ServiceError.NotFound => NotFound(result.Message ?? "Resource"),
        ServiceError.Forbidden => Forbidden(result.Message ?? "Not allowed."),
        ServiceError.Validation => Validation(result.Field ?? "request", result.Message ?? "Invalid."),
        ServiceError.Conflict => Conflict(result.Message ?? "Conflict."),
        ServiceError.Unavailable => Results.Problem(statusCode: StatusCodes.Status502BadGateway, title: "Bad Gateway", detail: result.Message),
        _ => throw new InvalidOperationException($"Unknown error {result.Error}."),
    };

    public static IResult Ok<T>(ServiceResult<T> result) => From(result, value => Results.Ok(value));

    public static IResult NoContent<T>(ServiceResult<T> result) => From(result, _ => Results.NoContent());
}
