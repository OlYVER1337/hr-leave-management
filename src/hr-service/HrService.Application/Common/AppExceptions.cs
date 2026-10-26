namespace HrService.Application.Common;
public abstract class AppException(string code, string message, int statusCode) : Exception(message)
{
    public string Code { get; } = code;
    public int StatusCode { get; } = statusCode;

}

public sealed class NotFoundException(string message) : AppException("Not found", message, 404);
public sealed class ConflictException(string message) : AppException("Conflict", message, 409);
public sealed class UnauthorizedException(string message) : AppException("Unauthorized", message, 401);
public sealed class ForbiddenException(string message = "You do not have permission to perform this action.")
    : AppException("Forbidden", message, 403);
public sealed class RequestValidationException(IReadOnlyDictionary<string, string[]> errors)
    : AppException("Validation failed", "Invalid request data", 422)
{
    public IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}