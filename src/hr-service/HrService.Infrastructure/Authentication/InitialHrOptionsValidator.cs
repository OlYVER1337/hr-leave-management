using Microsoft.Extensions.Options;

namespace HrService.Infrastructure.Authentication;

public sealed class InitialHrOptionsValidator : IValidateOptions<InitialHrOptions>
{
    public ValidateOptionsResult Validate(string? name, InitialHrOptions options)
    {
        if (!options.Enabled)
        {
            return ValidateOptionsResult.Success;
        }

        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.Email) || options.Email.Length > 200)
        {
            failures.Add("InitialHr:Email is required and must not exceed 200 characters.");
        }

        if (string.IsNullOrWhiteSpace(options.Password) ||
            options.Password.Length is < 12 or > 30)
        {
            failures.Add("InitialHr:Password must contain between 12 and 30 characters.");
        }

        if (string.IsNullOrWhiteSpace(options.FullName) || options.FullName.Trim().Length > 150)
        {
            failures.Add("InitialHr:FullName is required and must not exceed 150 characters.");
        }

        if (string.IsNullOrWhiteSpace(options.Code) || options.Code.Trim().Length > 20)
        {
            failures.Add("InitialHr:Code is required and must not exceed 20 characters.");
        }

        if (string.IsNullOrWhiteSpace(options.DepartmentName) ||
            options.DepartmentName.Trim().Length > 100)
        {
            failures.Add("InitialHr:DepartmentName is required and must not exceed 100 characters.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
