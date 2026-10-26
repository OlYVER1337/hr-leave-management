namespace HrService.Application.Dtos;

public sealed record  AuthenticationResponse(
    string AccessToken,
    DateTimeOffset ExpiresAtUtc,
    string FullName,
    string Email,
    Guid UserId,
    string Role
);