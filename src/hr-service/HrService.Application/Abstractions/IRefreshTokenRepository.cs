using HrService.Domain.Entities;

namespace HrService.Application.Abstractions;

public interface IRefreshTokenRepository
{
    void Add(RefreshToken refreshToken);

    Task<Employee?> RotateAsync(
        string tokenHash,
        RefreshToken replacement,
        DateTime nowUtc,
        CancellationToken cancellationToken = default);

    Task RevokeAsync(
        string tokenHash,
        DateTime revokedAtUtc,
        CancellationToken cancellationToken = default);

    Task RevokeAllAsync(
        Guid employeeId,
        DateTime revokedAtUtc,
        CancellationToken cancellationToken = default);
}
