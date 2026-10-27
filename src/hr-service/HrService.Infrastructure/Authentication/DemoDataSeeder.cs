using HrService.Application.Abstractions;
using HrService.Domain.Entities;
using HrService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HrService.Infrastructure.Authentication;

public sealed class DemoDataSeeder(
    HrDbContext dbContext,
    IPasswordHasher passwordHasher,
    TimeProvider timeProvider,
    IOptions<DemoDataOptions> options,
    ILogger<DemoDataSeeder> logger)
{
    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        var seedOptions = options.Value;
        if (!seedOptions.Enabled)
        {
            return;
        }

        var departmentName = seedOptions.DepartmentName.Trim();
        var department = await dbContext.Departments
            .FirstOrDefaultAsync(item => item.Name == departmentName, cancellationToken);

        if (department is null)
        {
            department = new Department { Name = departmentName };
            dbContext.Departments.Add(department);
        }

        var manager = await FindOrCreateEmployeeAsync(
            seedOptions.Manager,
            Role.Manager,
            department,
            manager: null,
            cancellationToken);

        await SeedLeaveTypesAndPoliciesAsync(seedOptions.SickDaysPerYear, cancellationToken);

        await FindOrCreateEmployeeAsync(
            seedOptions.Employee,
            Role.Employee,
            department,
            manager,
            cancellationToken);

        await dbContext.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Demo accounts and leave catalog were seeded.");
    }

    private async Task<Employee> FindOrCreateEmployeeAsync(
        DemoAccountOptions account,
        Role role,
        Department department,
        Employee? manager,
        CancellationToken cancellationToken)
    {
        var email = account.Email.Trim().ToLowerInvariant();
        var existingEmployee = await dbContext.Employees
            .FirstOrDefaultAsync(employee => employee.Email == email, cancellationToken);

        if (existingEmployee is not null)
        {
            if (existingEmployee.Role != role || !existingEmployee.IsActive)
            {
                throw new InvalidOperationException(
                    $"Configured demo email '{email}' already belongs to an inactive or different-role employee.");
            }

            return existingEmployee;
        }

        var code = account.Code.Trim();
        if (await dbContext.Employees.AnyAsync(employee => employee.Code == code, cancellationToken))
        {
            throw new InvalidOperationException($"Configured demo employee code '{code}' is already in use.");
        }

        var employee = new Employee
        {
            Code = code,
            FullName = account.FullName.Trim(),
            Email = email,
            Role = role,
            HireDate = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime),
            IsActive = true,
            Department = department,
            Manager = manager
        };
        employee.PasswordHash = passwordHasher.Hash(employee, account.Password);

        dbContext.Employees.Add(employee);
        return employee;
    }

    private async Task SeedLeaveTypesAndPoliciesAsync(
        int sickDaysPerYear,
        CancellationToken cancellationToken)
    {
        var leaveTypes = new[]
        {
            new LeaveTypeSeed("ANNUAL", "Nghỉ phép năm", IsPaid: true, DeductsBalance: true, DaysPerYear: 12m),
            new LeaveTypeSeed("SICK", "Nghỉ ốm", IsPaid: true, DeductsBalance: true, DaysPerYear: sickDaysPerYear),
            new LeaveTypeSeed("UNPAID", "Nghỉ không lương", IsPaid: false, DeductsBalance: false, DaysPerYear: null)
        };
        var year = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime).Year;

        foreach (var seed in leaveTypes)
        {
            var leaveType = await dbContext.LeaveTypes
                .FirstOrDefaultAsync(item => item.Code == seed.Code, cancellationToken);

            if (leaveType is null)
            {
                leaveType = new LeaveType
                {
                    Code = seed.Code,
                    Name = seed.Name,
                    IsPaid = seed.IsPaid,
                    DeductsBalance = seed.DeductsBalance,
                    IsActive = true
                };
                dbContext.LeaveTypes.Add(leaveType);
            }

            if (seed.DaysPerYear is null)
            {
                continue;
            }

            var hasPolicy = leaveType.Id != 0 &&
                await dbContext.LeavePolicies.AnyAsync(
                    policy => policy.LeaveTypeId == leaveType.Id && policy.Year == year,
                    cancellationToken);

            if (!hasPolicy)
            {
                dbContext.LeavePolicies.Add(new LeavePolicy
                {
                    LeaveType = leaveType,
                    Year = year,
                    DaysPerYear = seed.DaysPerYear.Value
                });
            }
        }
    }

    private sealed record LeaveTypeSeed(
        string Code,
        string Name,
        bool IsPaid,
        bool DeductsBalance,
        decimal? DaysPerYear);
}
