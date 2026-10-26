using HrService.Application.Abstractions;
using HrService.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace HrService.Infrastructure.Persistence.Repositories;

public sealed class DepartmentRepository(HrDbContext dbContext) : IDepartmentRepository
{
    public async Task<Department?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        return await dbContext.Departments
            .FirstOrDefaultAsync(department => department.Id == id, ct);
    }

    public async Task<IReadOnlyList<Department>> ListAsync(CancellationToken ct = default)
    {
        return await dbContext.Departments
            .AsNoTracking()
            .OrderBy(department => department.Name)
            .ToListAsync(ct);
    }

    public async Task<bool> ExistsByNameAsync(
        string name,
        int? excludeId,
        CancellationToken ct = default)
    {
        var query = dbContext.Departments
            .AsNoTracking()
            .Where(department => department.Name == name);

        if (excludeId.HasValue)
        {
            query = query.Where(department => department.Id != excludeId.Value);
        }

        return await query.AnyAsync(ct);
    }

    public async Task<bool> HasEmployeesAsync(int id, CancellationToken ct = default)
    {
        return await dbContext.Employees
            .AsNoTracking()
            .AnyAsync(employee => employee.DepartmentId == id, ct);
    }

    public void Add(Department department)
    {
        dbContext.Departments.Add(department);
    }

    public void Remove(Department department)
    {
        dbContext.Departments.Remove(department);
    }
}
