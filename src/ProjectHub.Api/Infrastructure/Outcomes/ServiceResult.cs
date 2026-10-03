namespace ProjectHub.Api.Infrastructure.Outcomes;

public enum ServiceError
{
    None,
    NotFound,
    Forbidden,
    Validation,
    Conflict,

    /// <summary>An external system (e.g. Webex) did not do what was asked; nothing was changed here.</summary>
    Unavailable,
}

/// <summary>Outcome of a domain operation, mapped to HTTP only at the endpoint.</summary>
public sealed record ServiceResult<T>(T? Value, ServiceError Error, string? Field = null, string? Message = null)
{
    public bool Succeeded => Error == ServiceError.None;

    public static implicit operator ServiceResult<T>(T value) => new(value, ServiceError.None);

    public static implicit operator ServiceResult<T>(ServiceFailure failure) =>
        new(default, failure.Error, failure.Field, failure.Message);
}

public sealed record ServiceFailure(ServiceError Error, string? Field = null, string? Message = null)
{
    public static ServiceFailure NotFound(string resource) => new(ServiceError.NotFound, Message: resource);

    public static ServiceFailure Forbidden(string message) => new(ServiceError.Forbidden, Message: message);

    public static ServiceFailure Invalid(string field, string message) => new(ServiceError.Validation, field, message);

    public static ServiceFailure Conflict(string message) => new(ServiceError.Conflict, Message: message);

    public static ServiceFailure Unavailable(string message) => new(ServiceError.Unavailable, Message: message);

    /// <summary>The caller's copy is outdated; the client reloads and retries.</summary>
    public static ServiceFailure StaleVersion(long currentVersion) =>
        Conflict($"The resource was changed by someone else (current version {currentVersion}). Reload and try again.");
}

/// <summary>Marker value for operations without a result body.</summary>
public sealed record Done
{
    public static readonly Done Value = new();
}
