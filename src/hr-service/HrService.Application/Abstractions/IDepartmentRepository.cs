namespace HrService.Application.Abstractions;

using HrService.Domain.Entities;
public interface IDepartmentRepository
{
    Task<Department?> GetByIdAsync(int id, CancellationToken ct = default);
    Task<IReadOnlyList<Department>> ListAsync(CancellationToken ct = default);
    Task<bool> ExistsByNameAsync(string name, int? excludeId, CancellationToken ct = default);
    Task<bool> HasEmployeesAsync(int id, CancellationToken ct = default);
    void Add(Department department);
    void Remove(Department department);
}
