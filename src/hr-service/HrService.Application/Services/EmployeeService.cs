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
    private const int MaximumPasswordLength = 30;

    public async Task<IReadOnlyList<EmployeeResponse>> GetAllAsync(
        CancellationToken cancellationToken = default)
    {
        var employees = await employeeRepository.ListAsync(cancellationToken);

        return employees.Select(ToResponse).ToArray();
    }

    public async Task<EmployeeResponse> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var employee = await employeeRepository.GetByIdAsync(id, cancellationToken);
        return employee is null
            ? throw new NotFoundException($"Employee with id '{id}' was not found.")
            : ToResponse(employee);
    }

    public async Task<EmployeeLookupResponse> GetLookupAsync(
        Guid id,
        Guid requesterId,
        bool requesterIsHr,
        CancellationToken cancellationToken = default)
    {
        if (!requesterIsHr &&
            requesterId != id &&
            !await employeeRepository.IsDirectManagerOfAsync(requesterId, id, cancellationToken))
        {
            throw new ForbiddenException();
        }

        var employee = await GetByIdAsync(id, cancellationToken);
        return ToLookupResponse(employee);
    }

    public async Task<IReadOnlyList<EmployeeLookupResponse>> GetTeamLookupAsync(
        Guid managerId,
        CancellationToken cancellationToken = default)
    {
        var employees = await GetTeamAsync(managerId, cancellationToken);
        return employees.Select(ToLookupResponse).ToArray();
    }

    public async Task<IReadOnlyList<EmployeeResponse>> GetTeamAsync(
        Guid managerId,
        CancellationToken cancellationToken = default)
    {
        var employees = await employeeRepository.GetDirectReportsAsync(managerId, cancellationToken);
        return employees.Select(ToResponse).ToArray();
    }

    public async Task ChangePasswordAsync(
        Guid id,
        ChangeEmployeePasswordRequest request,
        CancellationToken cancellationToken = default)
    {
        var newPassword = request.NewPassword;
        if (string.IsNullOrEmpty(newPassword) ||
            newPassword.Length < MinimumPasswordLength ||
            newPassword.Length > MaximumPasswordLength)
        {
            throw Problems.Invalid(
                "newPassword",
                $"Password must contain between {MinimumPasswordLength} and {MaximumPasswordLength} characters.");
        }

        var employee = await employeeRepository.GetForUpdateByIdAsync(id, cancellationToken)
            ?? throw new NotFoundException($"Employee with id '{id}' was not found.");

        if (!employee.IsActive ||
            string.IsNullOrEmpty(request.CurrentPassword) ||
            !passwordHasher.Verify(employee, request.CurrentPassword))
        {
            throw new UnauthorizedException("Current password is incorrect.");
        }

        employee.PasswordHash = passwordHasher.Hash(employee, newPassword);
        await unitOfWork.SaveChangesAsync(cancellationToken);
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

        if (await employeeRepository.ExistsByCodeAsync(code!, cancellationToken: cancellationToken))
        {
            throw new ConflictException($"Employee code '{code}' is already in use.");
        }

        if (await employeeRepository.ExistsByEmailAsync(email!, cancellationToken: cancellationToken))
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

    public async Task<EmployeeResponse> UpdateAsync(
        Guid id,
        UpdateEmployeeRequest request,
        CancellationToken cancellationToken = default)
    {
        var employee = await employeeRepository.GetForUpdateByIdAsync(id, cancellationToken)
            ?? throw new NotFoundException($"Employee with id '{id}' was not found.");

        var code = request.Code?.Trim();
        var fullName = request.FullName?.Trim();
        var email = request.Email?.Trim().ToLowerInvariant();
        var roleName = request.Role?.Trim();
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

        if (!Enum.TryParse<Role>(roleName, ignoreCase: true, out var role) || !Enum.IsDefined(role))
        {
            errors.Add("role", "Role must be Employee, Manager, or HR.");
        }

        if (request.HireDate == default)
        {
            errors.Add("hireDate", "Hire date is required.");
        }

        errors.ThrowIfAny();

        if (await employeeRepository.ExistsByCodeAsync(code!, id, cancellationToken))
        {
            throw new ConflictException($"Employee code '{code}' is already in use.");
        }

        if (await employeeRepository.ExistsByEmailAsync(email!, id, cancellationToken))
        {
            throw new ConflictException($"Email '{email}' is already in use.");
        }

        if (employee.Role == Role.Manager &&
            role != Role.Manager &&
            await employeeRepository.HasDirectReportsAsync(id, cancellationToken))
        {
            throw new ConflictException("An employee with direct reports cannot be changed from the Manager role.");
        }

        var department = await departmentRepository.GetByIdAsync(
            request.DepartmentId,
            cancellationToken)
            ?? throw new NotFoundException($"Department with id '{request.DepartmentId}' was not found.");

        Employee? manager = null;
        if (request.ManagerId.HasValue)
        {
            if (request.ManagerId.Value == id)
            {
                throw Problems.Invalid("managerId", "An employee cannot be their own manager.");
            }

            manager = await employeeRepository.GetActiveManagerByIdAsync(
                request.ManagerId.Value,
                cancellationToken);
            if (manager is null)
            {
                throw Problems.Invalid("managerId", "Manager must reference an active employee with the Manager role.");
            }
        }

        employee.Code = code!;
        employee.FullName = fullName!;
        employee.Email = email!;
        employee.Role = role;
        employee.HireDate = request.HireDate;
        employee.Department = department;
        employee.Manager = manager;

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return ToResponse(employee);
    }

    public async Task<EmployeeResponse> DeactivateAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var employee = await employeeRepository.GetForUpdateByIdAsync(id, cancellationToken)
            ?? throw new NotFoundException($"Employee with id '{id}' was not found.");

        if (employee.IsActive)
        {
            employee.IsActive = false;
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return ToResponse(employee);
    }

    public async Task ResetPasswordAsync(
        Guid id,
        ResetEmployeePasswordRequest request,
        CancellationToken cancellationToken = default)
    {
        var password = request.NewPassword;
        if (string.IsNullOrEmpty(password) ||
            password.Length < MinimumPasswordLength ||
            password.Length > MaximumPasswordLength)
        {
            throw Problems.Invalid(
                "newPassword",
                $"Password must contain between {MinimumPasswordLength} and {MaximumPasswordLength} characters.");
        }

        var employee = await employeeRepository.GetForUpdateByIdAsync(id, cancellationToken)
            ?? throw new NotFoundException($"Employee with id '{id}' was not found.");

        employee.PasswordHash = passwordHasher.Hash(employee, password);
        await unitOfWork.SaveChangesAsync(cancellationToken);
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

    private static EmployeeLookupResponse ToLookupResponse(EmployeeResponse employee) =>
        new(
            employee.Id,
            employee.Code,
            employee.FullName,
            employee.Role,
            employee.IsActive,
            employee.DepartmentId,
            employee.ManagerId);
}
