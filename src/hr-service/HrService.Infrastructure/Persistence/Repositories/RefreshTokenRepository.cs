using HrService.Application.Abstractions;
using HrService.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace HrService.Infrastructure.Persistence.Repositories;

public sealed class RefreshTokenRepository(HrDbContext dbContext) : IRefreshTokenRepository
{
    public void Add(RefreshToken refreshToken)
    {
        dbContext.RefreshTokens.Add(refreshToken);
    }

    public async Task<Employee?> RotateAsync(
        string tokenHash,
        RefreshToken replacement,
        DateTime nowUtc,
        CancellationToken cancellationToken = default)
    {
        var strategy = dbContext.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
            var current = await dbContext.RefreshTokens
                .Include(token => token.Employee)
                .SingleOrDefaultAsync(token => token.TokenHash == tokenHash, cancellationToken);
            if (current is null || current.RevokedAtUtc.HasValue || current.ExpiresAtUtc <= nowUtc)
            {
                await transaction.RollbackAsync(cancellationToken);
                return null;
            }

            var updated = await dbContext.RefreshTokens
                .Where(token =>
                    token.Id == current.Id &&
                    token.RevokedAtUtc == null &&
                    token.ExpiresAtUtc > nowUtc)
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(token => token.RevokedAtUtc, nowUtc)
                        .SetProperty(token => token.ReplacedByTokenHash, replacement.TokenHash),
                    cancellationToken);
            if (updated != 1)
            {
                await transaction.RollbackAsync(cancellationToken);
                return null;
            }

            if (current.Employee is not { IsActive: true } employee)
            {
                await transaction.CommitAsync(cancellationToken);
                return null;
            }

            replacement.EmployeeId = employee.Id;
            dbContext.RefreshTokens.Add(replacement);
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return employee;
        });
    }

    public async Task RevokeAsync(
        string tokenHash,
        DateTime revokedAtUtc,
        CancellationToken cancellationToken = default)
    {
        var token = await dbContext.RefreshTokens
            .FirstOrDefaultAsync(item => item.TokenHash == tokenHash, cancellationToken);
        if (token is not null && !token.RevokedAtUtc.HasValue)
        {
            token.RevokedAtUtc = revokedAtUtc;
        }
    }

    public async Task RevokeAllAsync(
        Guid employeeId,
        DateTime revokedAtUtc,
        CancellationToken cancellationToken = default)
    {
        var tokens = await dbContext.RefreshTokens
            .Where(item => item.EmployeeId == employeeId && item.RevokedAtUtc == null)
            .ToListAsync(cancellationToken);
        foreach (var token in tokens)
        {
            token.RevokedAtUtc = revokedAtUtc;
        }
    }
}
