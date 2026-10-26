namespace HrService.Infrastructure.Authentication;

public sealed class InitialHrOptions
{
    public const string SectionName = "InitialHr";

    public bool Enabled { get; init; }
    public string Email { get; init; } = string.Empty;
    public string Password { get; init; } = string.Empty;
    public string FullName { get; init; } = string.Empty;
    public string Code { get; init; } = string.Empty;
    public string DepartmentName { get; init; } = "HR";
}
