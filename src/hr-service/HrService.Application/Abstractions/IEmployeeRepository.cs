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

    Task<(IReadOnlyList<Employee> Items, int TotalCount)> ListVisiblePageAsync(
        Guid actorId,
        bool includeSelf,
        bool includeTeam,
        bool includeAll,
        int page,
        int pageSize,
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
