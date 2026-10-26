using HrService.Application.Abstractions;
using HrService.Application.Common;
using HrService.Application.Dtos;
using HrService.Domain.Entities;

namespace HrService.Application.Services;

public sealed class DepartmentService(
    IDepartmentRepository departmentRepository,
    IUnitOfWork unitOfWork)
{
    private const int MaxNameLength = 100;

    public async Task<IReadOnlyList<DepartmentResponse>> GetAllAsync(
        CancellationToken cancellationToken = default)
    {
        var departments = await departmentRepository.ListAsync(cancellationToken);
        return departments.Select(ToResponse).ToArray();
    }

    public async Task<DepartmentResponse> GetByIdAsync(
        int id,
        CancellationToken cancellationToken = default)
    {
        var department = await GetDepartmentEntityAsync(id, cancellationToken);
        return ToResponse(department);
    }

    public async Task<DepartmentResponse> CreateAsync(
        string name,
        CancellationToken cancellationToken = default)
    {
        var normalizedName = NormalizeName(name);

        if (await departmentRepository.ExistsByNameAsync(
                normalizedName,
                excludeId: null,
                cancellationToken))
        {
            throw new ConflictException($"Department '{normalizedName}' already exists.");
        }

        var department = new Department { Name = normalizedName };
        departmentRepository.Add(department);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return ToResponse(department);
    }

    public async Task<DepartmentResponse> UpdateAsync(
        int id,
        string name,
        CancellationToken cancellationToken = default)
    {
        var department = await GetDepartmentEntityAsync(id, cancellationToken);
        var normalizedName = NormalizeName(name);

        if (await departmentRepository.ExistsByNameAsync(
                normalizedName,
                excludeId: id,
                cancellationToken))
        {
            throw new ConflictException($"Department '{normalizedName}' already exists.");
        }

        department.Name = normalizedName;
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return ToResponse(department);
    }

    public async Task DeleteAsync(
        int id,
        CancellationToken cancellationToken = default)
    {
        var department = await GetDepartmentEntityAsync(id, cancellationToken);

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

    private async Task<Department> GetDepartmentEntityAsync(
        int id,
        CancellationToken cancellationToken)
    {
        var department = await departmentRepository.GetByIdAsync(id, cancellationToken);
        return department ?? throw new NotFoundException($"Department with id '{id}' was not found.");
    }

    private static DepartmentResponse ToResponse(Department department) =>
        new(department.Id, department.Name);
}
