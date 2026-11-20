using System.ComponentModel.DataAnnotations.Schema;

namespace LeaveService.Domain.Entities;

public sealed class LeaveBalance
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid EmployeeId { get; set; }
    public int LeaveTypeId { get; set; }
    public int Year { get; set; }
    public decimal PolicyDays { get; set; }
    public decimal Used { get; set; }
    public decimal Pending { get; set; }
    public byte[] RowVersion { get; set; } = [];

    [NotMapped]
    public decimal Entitled
    {
        get => Math.Max(PolicyDays, Used + Pending);
        set => PolicyDays = value;
    }

    [NotMapped]
    public decimal Available => Entitled - Used - Pending;
}
