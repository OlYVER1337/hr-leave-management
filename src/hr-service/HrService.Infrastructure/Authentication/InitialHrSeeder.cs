using HrService.Domain.Entities;
using HrService.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HrService.Infrastructure.Authentication;

public sealed class InitialHrSeeder(
    HrDbContext dbContext,
    IOptions<InitialHrOptions> options,
    ILogger<InitialHrSeeder> logger)
{
    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        var seedOptions = options.Value;
        if (!seedOptions.Enabled)
        {
            return;
        }

        var email = seedOptions.Email.Trim();
        if (await dbContext.Employees.AnyAsync(employee => employee.Email == email, cancellationToken))
        {
            logger.LogInformation("Initial HR account already exists; no account changes were made.");
            return;
        }

        var code = seedOptions.Code.Trim();
        if (await dbContext.Employees.AnyAsync(employee => employee.Code == code, cancellationToken))
        {
            throw new InvalidOperationException("Initial HR employee code is already assigned to another employee.");
        }

        var departmentName = seedOptions.DepartmentName.Trim();
        var department = await dbContext.Departments
            .FirstOrDefaultAsync(item => item.Name == departmentName, cancellationToken);

        if (department is null)
        {
            department = new Department { Name = departmentName };
            dbContext.Departments.Add(department);
        }

        var employee = new Employee
        {
            Code = code,
            FullName = seedOptions.FullName.Trim(),
            Email = email,
            Role = Role.HR,
            HireDate = DateOnly.FromDateTime(DateTime.UtcNow),
            IsActive = true,
            Department = department
        };
        employee.PasswordHash = new PasswordHasher<Employee>()
            .HashPassword(employee, seedOptions.Password);

        dbContext.Employees.Add(employee);
        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Initial HR account was created.");
    }
}
