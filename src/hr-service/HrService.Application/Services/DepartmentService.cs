using HrService.Application.Abstractions;
using HrService.Application.Common;
using HrService.Domain.Entities;

namespace HrService.Application.Services;

public sealed class DepartmentService(
    IDepratmentRepositoty departmentRepository,
    IUnitOfWork unitOfWork)
{
    private const int MaxNameLength = 100;

    public async Task<IReadOnlyList<Department>> GetAllAsync(
        CancellationToken cancellationToken = default)
    {
        return await departmentRepository.ListAsync(cancellationToken);
    }

    public async Task<Department> GetByIdAsync(
        int id,
        CancellationToken cancellationToken = default)
    {
        var department = await departmentRepository.GetByIdAsync(id, cancellationToken);

        return department ?? throw new NotFoundException($"Department with id '{id}' was not found.");
    }

    public async Task<Department> CreateAsync(
        string name,
        CancellationToken cancellationToken = default)
    {
        var normalizedName = NormalizeName(name);

        if (await departmentRepository.ExistByNameAsync(
                normalizedName,
                excludeId: null,
                cancellationToken))
        {
            throw new ConflictException($"Department '{normalizedName}' already exists.");
        }

        var department = new Department { Name = normalizedName };
        departmentRepository.Add(department);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return department;
    }

    public async Task<Department> UpdateAsync(
        int id,
        string name,
        CancellationToken cancellationToken = default)
    {
        var department = await GetByIdAsync(id, cancellationToken);
        var normalizedName = NormalizeName(name);

        if (await departmentRepository.ExistByNameAsync(
                normalizedName,
                excludeId: id,
                cancellationToken))
        {
            throw new ConflictException($"Department '{normalizedName}' already exists.");
        }

        department.Name = normalizedName;
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return department;
    }

    public async Task DeleteAsync(
        int id,
        CancellationToken cancellationToken = default)
    {
        var department = await GetByIdAsync(id, cancellationToken);

        if (await departmentRepository.HasEmployeesAsync(id, cancellationToken))
        {
            throw new ConflictException("A department assigned to employees cannot be deleted.");
        }

        departmentRepository.Remove(department);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private static string NormalizeName(string name)
    {
        var normalizedName = name?.Trim();

        if (string.IsNullOrWhiteSpace(normalizedName))
        {
            throw Problems.Invalid("name", "Department name is required.");
        }

        if (normalizedName.Length > MaxNameLength)
        {
            throw Problems.Invalid("name", $"Department name cannot exceed {MaxNameLength} characters.");
        }

        if (normalizedName.Length < 2)
        {
            throw Problems.Invalid("name", "Department name must contain at least 2 characters.");
        }

        return normalizedName;
    }
}
