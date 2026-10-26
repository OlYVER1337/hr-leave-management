using HrService.Application.Abstractions;
using HrService.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace HrService.Infrastructure.Persistence.Repositories;

public sealed class AuthenticationRepository(HrDbContext dbContext) : IAuthenticationRepository
{
    public async Task<Employee?> FindActiveByEmailAsync(
        string email,
        CancellationToken cancellationToken = default)
    {
        return await dbContext.Employees
            .AsNoTracking()
            .Where(employee => employee.Email == email && employee.IsActive)
            .FirstOrDefaultAsync(cancellationToken);
    }
}
