namespace LeaveService.Domain.Entities;

public sealed class LeaveRequest
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid EmployeeId { get; set; }
    public Guid? ApproverId { get; set; }
    public int DepartmentId { get; set; }
    public int LeaveTypeId { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public decimal TotalDays { get; set; }
    public string Reason { get; set; } = string.Empty;
    public LeaveRequestStatus Status { get; set; } = LeaveRequestStatus.Pending;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? DecidedAtUtc { get; set; }
    public byte[] RowVersion { get; set; } = [];
    public ICollection<LeaveRequestHistory> Histories { get; set; } = new List<LeaveRequestHistory>();
    public ICollection<LeaveRequestYearAllocation> YearAllocations { get; set; } =
        new List<LeaveRequestYearAllocation>();
}
