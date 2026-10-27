using HrService.Domain.Entities;

namespace HrService.Application.Abstractions;

public interface IEmployeeRepository
{
    Task<IReadOnlyList<Employee>> ListAsync(CancellationToken cancellationToken = default);
    Task<Employee?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
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
    Task<Employee?> GetActiveManagerByIdAsync(Guid managerId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Employee>> GetDirectReportsAsync(
        Guid managerId,
        CancellationToken cancellationToken = default);
    Task<bool> HasDirectReportsAsync(Guid managerId, CancellationToken cancellationToken = default);
    void Add(Employee employee);
}
