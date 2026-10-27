using Microsoft.Extensions.Options;

namespace HrService.Infrastructure.Authentication;

public sealed class DemoDataOptionsValidator : IValidateOptions<DemoDataOptions>
{
    public ValidateOptionsResult Validate(string? name, DemoDataOptions options)
    {
        if (!options.Enabled)
        {
            return ValidateOptionsResult.Success;
        }

        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.DepartmentName) ||
            options.DepartmentName.Trim().Length > 100)
        {
            failures.Add("DemoData:DepartmentName is required and must not exceed 100 characters.");
        }

        if (options.SickDaysPerYear <= 0)
        {
            failures.Add("DemoData:SickDaysPerYear must be positive.");
        }

        ValidateAccount(options.Manager, "Manager", failures);
        ValidateAccount(options.Employee, "Employee", failures);

        if (!string.IsNullOrWhiteSpace(options.Manager.Email) &&
            !string.IsNullOrWhiteSpace(options.Employee.Email) &&
            string.Equals(
                options.Manager.Email.Trim(),
                options.Employee.Email.Trim(),
                StringComparison.OrdinalIgnoreCase))
        {
            failures.Add("DemoData manager and employee emails must be different.");
        }

        if (!string.IsNullOrWhiteSpace(options.Manager.Code) &&
            !string.IsNullOrWhiteSpace(options.Employee.Code) &&
            string.Equals(
                options.Manager.Code.Trim(),
                options.Employee.Code.Trim(),
                StringComparison.OrdinalIgnoreCase))
        {
            failures.Add("DemoData manager and employee codes must be different.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    private static void ValidateAccount(
        DemoAccountOptions account,
        string role,
        ICollection<string> failures)
    {
        if (string.IsNullOrWhiteSpace(account.Email) || account.Email.Length > 200)
        {
            failures.Add($"DemoData:{role}:Email is required and must not exceed 200 characters.");
        }

        if (string.IsNullOrWhiteSpace(account.Password) ||
            account.Password.Length is < 12 or > 30)
        {
            failures.Add($"DemoData:{role}:Password must contain between 12 and 30 characters.");
        }

        if (string.IsNullOrWhiteSpace(account.FullName) || account.FullName.Trim().Length > 150)
        {
            failures.Add($"DemoData:{role}:FullName is required and must not exceed 150 characters.");
        }

        if (string.IsNullOrWhiteSpace(account.Code) || account.Code.Trim().Length > 20)
        {
            failures.Add($"DemoData:{role}:Code is required and must not exceed 20 characters.");
        }
    }
}
