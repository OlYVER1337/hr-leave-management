using HrService.Application.Abstractions;
using HrService.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace HrService.Infrastructure.Persistence.Repositories;

public sealed class EmployeeRepository(HrDbContext dbContext) : IEmployeeRepository
{
    public async Task<IReadOnlyList<Employee>> ListAsync(CancellationToken cancellationToken = default)
    {
        return await dbContext.Employees
            .AsNoTracking()
            .Include(employee => employee.Department)
            .Include(employee => employee.Manager)
            .OrderBy(employee => employee.FullName)
            .ToListAsync(cancellationToken);
    }

    public Task<Employee?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return dbContext.Employees
            .AsNoTracking()
            .Include(employee => employee.Department)
            .Include(employee => employee.Manager)
            .FirstOrDefaultAsync(employee => employee.Id == id, cancellationToken);
    }

    public Task<Employee?> GetForUpdateByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return dbContext.Employees
            .Include(employee => employee.Department)
            .Include(employee => employee.Manager)
            .FirstOrDefaultAsync(employee => employee.Id == id, cancellationToken);
    }

    public Task<Employee?> GetActiveByEmailAsync(
        string normalizedEmail,
        CancellationToken cancellationToken = default)
    {
        return dbContext.Employees
            .AsNoTracking()
            .FirstOrDefaultAsync(
                employee => employee.Email == normalizedEmail && employee.IsActive,
                cancellationToken);
    }

    public Task<bool> ExistsByCodeAsync(
        string code,
        Guid? excludeEmployeeId = null,
        CancellationToken cancellationToken = default)
    {
        var query = dbContext.Employees
            .AsNoTracking()
            .Where(employee => employee.Code == code);

        if (excludeEmployeeId.HasValue)
        {
            query = query.Where(employee => employee.Id != excludeEmployeeId.Value);
        }

        return query.AnyAsync(cancellationToken);
    }

    public Task<bool> ExistsByEmailAsync(
        string email,
        Guid? excludeEmployeeId = null,
        CancellationToken cancellationToken = default)
    {
        var query = dbContext.Employees
            .AsNoTracking()
            .Where(employee => employee.Email == email);

        if (excludeEmployeeId.HasValue)
        {
            query = query.Where(employee => employee.Id != excludeEmployeeId.Value);
        }

        return query.AnyAsync(cancellationToken);
    }

    public Task<Employee?> GetActiveManagerByIdAsync(
        Guid managerId,
        CancellationToken cancellationToken = default)
    {
        return dbContext.Employees
            .Include(employee => employee.Department)
            .FirstOrDefaultAsync(
                employee => employee.Id == managerId && employee.Role == Role.Manager && employee.IsActive,
                cancellationToken);
    }

    public async Task<IReadOnlyList<Employee>> GetDirectReportsAsync(
        Guid managerId,
        CancellationToken cancellationToken = default)
    {
        return await dbContext.Employees
            .AsNoTracking()
            .Include(employee => employee.Department)
            .Include(employee => employee.Manager)
            .Where(employee => employee.ManagerId == managerId && employee.IsActive)
            .OrderBy(employee => employee.FullName)
            .ToListAsync(cancellationToken);
    }

    public Task<bool> HasDirectReportsAsync(
        Guid managerId,
        CancellationToken cancellationToken = default)
    {
        return dbContext.Employees
            .AsNoTracking()
            .AnyAsync(employee => employee.ManagerId == managerId, cancellationToken);
    }

    public void Add(Employee employee)
    {
        dbContext.Employees.Add(employee);
    }
}
