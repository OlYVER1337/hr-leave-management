namespace LeaveService.Domain.Entities;

public sealed class LeaveRequestYearAllocation
{
    public Guid LeaveRequestId { get; set; }
    public LeaveRequest? LeaveRequest { get; set; }
    public int Year { get; set; }
    public decimal Days { get; set; }
}
