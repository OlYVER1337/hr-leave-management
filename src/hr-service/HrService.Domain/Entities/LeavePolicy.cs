namespace HrService.Domain.Entities;

public class LeavePolicy
{
    public int Id { get; set; }
    public int LeaveTypeId{ get; set; }
    public LeaveType? LeaveType { get; set; }
    public int Year { get; set; }
    public decimal DaysPerYear { get; set; }
}