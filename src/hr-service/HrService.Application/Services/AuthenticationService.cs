using HrService.Application.Abstractions;
using HrService.Application.Common;
using HrService.Application.Dtos;
using HrService.Domain.Entities;
using System.Security.Cryptography;
using System.Text;

namespace HrService.Application.Services;

public sealed class AuthenticationService(
    IEmployeeRepository employeeRepository,
    IPasswordHasher passwordHasher,
    IJwtTokenGenerator jwtTokenGenerator,
    IRefreshTokenRepository refreshTokenRepository,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider)
{
    private const string InvalidCredentialsMessage = "Invalid email or password.";
    public static readonly TimeSpan RefreshTokenLifetime = TimeSpan.FromDays(7);

    public async Task<AuthenticationSession> AuthenticateAsync(
        string email,
        string password,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrEmpty(password))
        {
            throw new UnauthorizedException(InvalidCredentialsMessage);
        }

        var normalizedEmail = email.Trim().ToLowerInvariant();
        var employee = await employeeRepository.GetActiveByEmailAsync(
            normalizedEmail,
            cancellationToken);

        if (employee is null || !passwordHasher.Verify(employee, password))
        {
            throw new UnauthorizedException(InvalidCredentialsMessage);
        }

        var now = timeProvider.GetUtcNow();
        var refreshToken = CreateRefreshToken(employee.Id, now);
        refreshTokenRepository.Add(refreshToken.Entity);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new AuthenticationSession(
            jwtTokenGenerator.GenerateToken(employee),
            refreshToken.Token,
            now.Add(RefreshTokenLifetime));
    }

    public async Task<AuthenticationSession> RefreshAsync(
        string? token,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new UnauthorizedException(InvalidCredentialsMessage);
        }

        var now = timeProvider.GetUtcNow();
        var replacement = CreateRefreshToken(Guid.Empty, now);
        var employee = await refreshTokenRepository.RotateAsync(
            HashToken(token),
            replacement.Entity,
            now.UtcDateTime,
            cancellationToken);
        if (employee is null)
        {
            throw new UnauthorizedException(InvalidCredentialsMessage);
        }

        return new AuthenticationSession(
            jwtTokenGenerator.GenerateToken(employee),
            replacement.Token,
            now.Add(RefreshTokenLifetime));
    }

    public async Task RevokeAsync(
        string? token,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return;
        }

        await refreshTokenRepository.RevokeAsync(
            HashToken(token),
            timeProvider.GetUtcNow().UtcDateTime,
            cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task RevokeAllAsync(
        Guid employeeId,
        CancellationToken cancellationToken = default)
    {
        await refreshTokenRepository.RevokeAllAsync(
            employeeId,
            timeProvider.GetUtcNow().UtcDateTime,
            cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private static (string Token, RefreshToken Entity) CreateRefreshToken(
        Guid employeeId,
        DateTimeOffset now)
    {
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
        return (
            token,
            new RefreshToken
            {
                EmployeeId = employeeId,
                TokenHash = HashToken(token),
                CreatedAtUtc = now.UtcDateTime,
                ExpiresAtUtc = now.Add(RefreshTokenLifetime).UtcDateTime
            });
    }

    private static string HashToken(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
