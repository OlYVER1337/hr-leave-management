using HrService.Domain.Entities;

namespace HrService.Application.Abstractions;

public interface IEmployeeRepository
{
    Task<IReadOnlyList<Employee>> ListAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Employee>> ListVisibleAsync(
        Guid actorId,
        bool includeSelf,
        bool includeTeam,
        bool includeAll,
        CancellationToken cancellationToken = default);

    Task<(IReadOnlyList<EmployeeReadModel> Items, int TotalCount)> ListVisiblePageAsync(
        Guid actorId,
        bool includeSelf,
        bool includeTeam,
        bool includeAll,
        string? search,
        Role? role,
        bool? isActive,
        int? departmentId,
        string sortBy,
        bool descending,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<EmployeeReadModel>> ListVisibleByIdsAsync(
        Guid actorId,
        bool includeSelf,
        bool includeTeam,
        bool includeAll,
        IReadOnlyCollection<Guid> employeeIds,
        CancellationToken cancellationToken = default);
    Task<Employee?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<bool> IsDirectManagerOfAsync(
        Guid managerId,
        Guid employeeId,
        CancellationToken cancellationToken = default);
    Task<Employee?> GetForUpdateByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Employee?> GetActiveByEmailAsync(string normalizedEmail, CancellationToken cancellationToken = default);
    Task<bool> ExistsByCodeAsync(
        string code,
        Guid? excludeEmployeeId = null,
        CancellationToken cancellationToken = default);
    Task<bool> ExistsByEmailAsync(
        string email,
        Guid? excludeEmployeeId = null,
        CancellationToken cancellationToken = default);
    Task<Employee?> GetActiveManagerByIdAsync(
        Guid managerId,
        DateTime utcNow,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Employee>> GetDirectReportsAsync(
        Guid managerId,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Guid>> GetDirectReportIdsAsync(
        Guid managerId,
        CancellationToken cancellationToken = default);
    Task<bool> HasDirectReportsAsync(Guid managerId, CancellationToken cancellationToken = default);
    void Add(Employee employee);
}

// Chỉ các cột cần để trả về API: không đọc PasswordHash, không nạp nguyên entity Manager/Department.
public sealed record EmployeeReadModel(
    Guid Id,
    string Code,
    string FullName,
    string Email,
    Role Role,
    DateOnly HireDate,
    bool IsActive,
    int DepartmentId,
    string DepartmentName,
    Guid? ManagerId,
    string? ManagerName);
