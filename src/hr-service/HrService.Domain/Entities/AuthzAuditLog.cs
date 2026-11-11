namespace HrService.Domain.Entities;

public sealed class AuthzAuditLog
{
    public long Id { get; set; }

    public Guid ActorEmployeeId { get; set; }
    public Guid? TargetEmployeeId { get; set; }

    public string Action { get; set; } = string.Empty;
    public string TargetType { get; set; } = string.Empty;
    public string? TargetId { get; set; }

    public string? BeforeJson { get; set; }
    public string? AfterJson { get; set; }
    public string? CorrelationId { get; set; }

    public DateTime CreatedAtUtc { get; set; }
}
