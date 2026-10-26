using HrService.Application.Abstractions;
using HrService.Application.Common;
using HrService.Application.Dtos;

namespace HrService.Application.Services;

public sealed class AuthenticationService(
    IAuthenticationRepository authenticationRepository,
    IPasswordHashVerifier passwordHashVerifier,
    IJwtTokenGenerator jwtTokenGenerator)
{
    private const string InvalidCredentialsMessage = "Invalid email or password.";

    public async Task<AuthenticationResponse> AuthenticateAsync(
        string email,
        string password,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrEmpty(password))
        {
            throw new UnauthorizedException(InvalidCredentialsMessage);
        }

        var employee = await authenticationRepository.FindActiveByEmailAsync(
            email.Trim(),
            cancellationToken);

        if (employee is null || !passwordHashVerifier.Verify(employee, password))
        {
            throw new UnauthorizedException(InvalidCredentialsMessage);
        }

        return jwtTokenGenerator.GenerateToken(employee);
    }
}
