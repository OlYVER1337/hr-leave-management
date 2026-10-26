using HrService.Application.Abstractions;

namespace HrService.Infrastructure.Persistence;

public sealed class UnitOfWork(HrDbContext dbContext) : IUnitOfWork
{
    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        return dbContext.SaveChangesAsync(cancellationToken);
    }
}
