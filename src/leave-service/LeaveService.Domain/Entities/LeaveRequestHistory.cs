namespace LeaveService.Domain.Entities;

public sealed class LeaveRequestHistory
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid LeaveRequestId { get; set; }
    public LeaveRequest? LeaveRequest { get; set; }
    public Guid ActorId { get; set; }
    public string Action { get; set; } = string.Empty;
    public LeaveRequestStatus? FromStatus { get; set; }
    public LeaveRequestStatus ToStatus { get; set; }
    public string? Note { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}
