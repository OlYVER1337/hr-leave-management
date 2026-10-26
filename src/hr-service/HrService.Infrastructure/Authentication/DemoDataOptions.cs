namespace HrService.Infrastructure.Authentication;

public sealed class DemoDataOptions
{
    public const string SectionName = "DemoData";

    public bool Enabled { get; init; }
    public string DepartmentName { get; init; } = "Demo";
    public int SickDaysPerYear { get; init; } = 10;
    public DemoAccountOptions Manager { get; init; } = new();
    public DemoAccountOptions Employee { get; init; } = new();
}

public sealed class DemoAccountOptions
{
    public string Email { get; init; } = string.Empty;
    public string Password { get; init; } = string.Empty;
    public string FullName { get; init; } = string.Empty;
    public string Code { get; init; } = string.Empty;
}
