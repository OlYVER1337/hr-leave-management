namespace LeaveService.Application.Common;

public abstract class LeaveAppException(string code, string message, int statusCode)
    : Exception(message)
{
    public string Code { get; } = code;
    public int StatusCode { get; } = statusCode;
}

public sealed class LeaveNotFoundException(string message)
    : LeaveAppException("Not found", message, 404);

public sealed class LeaveConflictException(string message)
    : LeaveAppException("Conflict", message, 409);

public sealed class LeaveUnauthorizedException(string message)
    : LeaveAppException("Unauthorized", message, 401);

public sealed class LeaveForbiddenException(string message)
    : LeaveAppException("Forbidden", message, 403);

public sealed class LeaveValidationException(
    IReadOnlyDictionary<string, string[]> errors)
    : LeaveAppException("Validation failed", "Invalid request data", 422)
{
    public IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}

public sealed class DependencyUnavailableException(string dependency, Exception? inner = null)
    : Exception($"{dependency} is temporarily unavailable.", inner);
