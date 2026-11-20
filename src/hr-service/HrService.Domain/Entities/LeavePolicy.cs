namespace HrService.Domain.Entities;

public class LeavePolicy
{
    public int Id { get; set; }
    public int LeaveTypeId { get; set; }
    public LeaveType? LeaveType { get; set; }
    public int Year { get; set; }
    public decimal DaysPerYear { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public string Reason { get; set; } = string.Empty;
}
