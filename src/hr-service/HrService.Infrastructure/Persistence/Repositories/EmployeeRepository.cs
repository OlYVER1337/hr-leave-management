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

    public Task<bool> ExistsByCodeAsync(string code, CancellationToken cancellationToken = default)
    {
        return dbContext.Employees
            .AsNoTracking()
            .AnyAsync(employee => employee.Code == code, cancellationToken);
    }

    public Task<bool> ExistsByEmailAsync(string email, CancellationToken cancellationToken = default)
    {
        return dbContext.Employees
            .AsNoTracking()
            .AnyAsync(employee => employee.Email == email, cancellationToken);
    }

    public Task<Department?> GetDepartmentByIdAsync(
        int departmentId,
        CancellationToken cancellationToken = default)
    {
        return dbContext.Departments
            .FirstOrDefaultAsync(department => department.Id == departmentId, cancellationToken);
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

    public void Add(Employee employee)
    {
        dbContext.Employees.Add(employee);
    }
}
