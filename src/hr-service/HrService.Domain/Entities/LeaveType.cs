namespace HrService.Domain.Entities;

public class LeaveType
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsPaid { get; set; }

    public bool DeductsBalance { get; set; } = true;
    public bool IsActive { get; set; } = true;
    public ICollection<LeavePolicy> Policies { get; set; } = new List<LeavePolicy>();
}