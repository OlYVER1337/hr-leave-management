using HrService.Application.Abstractions;
using HrService.Application.Common;
using HrService.Application.Dtos;
using HrService.Domain.Entities;
using System.Net.Mail;

namespace HrService.Application.Services;

public sealed class EmployeeService(
    IEmployeeRepository employeeRepository,
    IDepartmentRepository departmentRepository,
    IPasswordHasher passwordHasher,
    IUnitOfWork unitOfWork)
{
    private const int MinimumPasswordLength = 12;
    private const int MaximumPasswordLength = 128;

    public async Task<IReadOnlyList<EmployeeResponse>> GetAllAsync(
        CancellationToken cancellationToken = default)
    {
        var employees = await employeeRepository.ListAsync(cancellationToken);

        return employees
            .Select(employee => new EmployeeResponse(
                employee.Id,
                employee.Code,
                employee.FullName,
                employee.Email,
                employee.Role.ToString(),
                employee.HireDate,
                employee.IsActive,
                employee.DepartmentId,
                employee.Department?.Name ?? string.Empty,
                employee.ManagerId,
                employee.Manager?.FullName))
            .ToArray();
    }

    public async Task<EmployeeResponse> CreateAsync(
        CreateEmployeeRequest request,
        CancellationToken cancellationToken = default)
    {
        var code = request.Code?.Trim();
        var fullName = request.FullName?.Trim();
        var email = request.Email?.Trim().ToLowerInvariant();
        var roleName = request.Role?.Trim();
        var password = request.Password;
        var errors = new ValidationErrors();

        if (string.IsNullOrWhiteSpace(code) || code.Length > 20)
        {
            errors.Add("code", "Employee code is required and cannot exceed 20 characters.");
        }

        if (string.IsNullOrWhiteSpace(fullName) || fullName.Length > 150)
        {
            errors.Add("fullName", "Full name is required and cannot exceed 150 characters.");
        }

        if (string.IsNullOrWhiteSpace(email) || email.Length > 200 || !IsValidEmail(email))
        {
            errors.Add("email", "A valid email of at most 200 characters is required.");
        }

        if (string.IsNullOrEmpty(password) ||
            password.Length < MinimumPasswordLength ||
            password.Length > MaximumPasswordLength)
        {
            errors.Add(
                "password",
                $"Password must contain between {MinimumPasswordLength} and {MaximumPasswordLength} characters.");
        }

        if (!Enum.TryParse<Role>(roleName, ignoreCase: true, out var role) || !Enum.IsDefined(role))
        {
            errors.Add("role", "Role must be Employee, Manager, or HR.");
        }

        if (request.HireDate == default)
        {
            errors.Add("hireDate", "Hire date is required.");
        }

        errors.ThrowIfAny();

        if (await employeeRepository.ExistsByCodeAsync(code!, cancellationToken))
        {
            throw new ConflictException($"Employee code '{code}' is already in use.");
        }

        if (await employeeRepository.ExistsByEmailAsync(email!, cancellationToken))
        {
            throw new ConflictException($"Email '{email}' is already in use.");
        }

        var department = await departmentRepository.GetByIdAsync(
            request.DepartmentId,
            cancellationToken);
        if (department is null)
        {
            throw new NotFoundException($"Department with id '{request.DepartmentId}' was not found.");
        }

        Employee? manager = null;
        if (request.ManagerId.HasValue)
        {
            manager = await employeeRepository.GetActiveManagerByIdAsync(
                request.ManagerId.Value,
                cancellationToken);
            if (manager is null)
            {
                throw new RequestValidationException(new Dictionary<string, string[]>
                {
                    ["managerId"] = ["Manager must reference an active employee with the Manager role."]
                });
            }
        }

        var employee = new Employee
        {
            Code = code!,
            FullName = fullName!,
            Email = email!,
            Role = role,
            HireDate = request.HireDate,
            IsActive = true,
            Department = department,
            Manager = manager
        };
        employee.PasswordHash = passwordHasher.Hash(employee, password!);

        employeeRepository.Add(employee);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return ToResponse(employee);
    }

    private static bool IsValidEmail(string email)
    {
        try
        {
            return new MailAddress(email).Address == email;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static EmployeeResponse ToResponse(Employee employee) =>
        new(
            employee.Id,
            employee.Code,
            employee.FullName,
            employee.Email,
            employee.Role.ToString(),
            employee.HireDate,
            employee.IsActive,
            employee.DepartmentId,
            employee.Department?.Name ?? string.Empty,
            employee.ManagerId,
            employee.Manager?.FullName);
}
