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

    public async Task<IReadOnlyList<Employee>> ListVisibleAsync(
        Guid actorId,
        bool includeSelf,
        bool includeTeam,
        bool includeAll,
        CancellationToken cancellationToken = default)
    {
        if (actorId == Guid.Empty || (!includeSelf && !includeTeam && !includeAll))
            return Array.Empty<Employee>();

        return await dbContext.Employees
            .AsNoTracking()
            .Include(employee => employee.Department)
            .Include(employee => employee.Manager)
            .Where(employee =>
                includeAll ||
                (includeSelf && employee.Id == actorId) ||
                (includeTeam && employee.IsActive && employee.ManagerId == actorId))
            .OrderBy(employee => employee.FullName)
            .ToListAsync(cancellationToken);
    }

    public async Task<(IReadOnlyList<Employee> Items, int TotalCount)> ListVisiblePageAsync(
        Guid actorId,
        bool includeSelf,
        bool includeTeam,
        bool includeAll,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        if (actorId == Guid.Empty || (!includeSelf && !includeTeam && !includeAll))
            return (Array.Empty<Employee>(), 0);

        // Phạm vi áp trong WHERE; COUNT và OFFSET/FETCH đều chạy trên SQL, không tải toàn bộ bảng.
        var visible = dbContext.Employees
            .AsNoTracking()
            .Where(employee =>
                includeAll ||
                (includeSelf && employee.Id == actorId) ||
                (includeTeam && employee.IsActive && employee.ManagerId == actorId));

        var totalCount = await visible.CountAsync(cancellationToken);

        var items = await visible
            .Include(employee => employee.Department)
            .Include(employee => employee.Manager)
            .OrderBy(employee => employee.FullName)
            .ThenBy(employee => employee.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return (items, totalCount);
    }

    public Task<Employee?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return dbContext.Employees
            .AsNoTracking()
            .Include(employee => employee.Department)
            .Include(employee => employee.Manager)
            .FirstOrDefaultAsync(employee => employee.Id == id, cancellationToken);
    }

    public Task<bool> IsDirectManagerOfAsync(
        Guid managerId,
        Guid employeeId,
        CancellationToken cancellationToken = default)
    {
        return dbContext.Employees
            .AsNoTracking()
            .AnyAsync(
                employee => employee.Id == employeeId && employee.ManagerId == managerId,
                cancellationToken);
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
        DateTime utcNow,
        CancellationToken cancellationToken = default)
    {
        return dbContext.Employees
            .Include(employee => employee.Department)
            .FirstOrDefaultAsync(
                employee =>
                    employee.Id == managerId &&
                    employee.IsActive &&
                    employee.UserRoles.Any(assignment =>
                        assignment.RevokedAtUtc == null &&
                        (assignment.ExpiresAtUtc == null || assignment.ExpiresAtUtc > utcNow) &&
                        assignment.Role.IsActive &&
                        assignment.Role.RolePermissions.Any(rolePermission =>
                            rolePermission.Permission.IsActive &&
                            rolePermission.Permission.Code == "leaves.decide" &&
                            rolePermission.Scopes.Any(scope =>
                                scope.Scope == "Team" || scope.Scope == "All"))),
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

    public async Task<IReadOnlyList<Guid>> GetDirectReportIdsAsync(
        Guid managerId,
        CancellationToken cancellationToken = default)
    {
        return await dbContext.Employees
            .AsNoTracking()
            .Where(employee => employee.ManagerId == managerId && employee.IsActive)
            .OrderBy(employee => employee.Id)
            .Select(employee => employee.Id)
            .ToArrayAsync(cancellationToken);
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
