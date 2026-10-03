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
}
