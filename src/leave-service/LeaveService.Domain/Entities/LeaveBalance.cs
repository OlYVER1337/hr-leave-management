namespace LeaveService.Domain.Entities;

public sealed class LeaveBalance
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid EmployeeId { get; set; }
    public int LeaveTypeId { get; set; }
    public int Year { get; set; }
    public decimal Entitled { get; set; }
    public decimal Used { get; set; }
    public decimal Pending { get; set; }
    public byte[] RowVersion { get; set; } = [];
}
