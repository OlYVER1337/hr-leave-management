using HrService.Domain.Entities;

namespace HrService.Application.Abstractions;

public interface IEmployeeRepository
{
    Task<IReadOnlyList<Employee>> ListAsync(CancellationToken cancellationToken = default);
    Task<Employee?> GetActiveByEmailAsync(string normalizedEmail, CancellationToken cancellationToken = default);
    Task<bool> ExistsByCodeAsync(string code, CancellationToken cancellationToken = default);
    Task<bool> ExistsByEmailAsync(string email, CancellationToken cancellationToken = default);
    Task<Employee?> GetActiveManagerByIdAsync(Guid managerId, CancellationToken cancellationToken = default);
    void Add(Employee employee);
}
