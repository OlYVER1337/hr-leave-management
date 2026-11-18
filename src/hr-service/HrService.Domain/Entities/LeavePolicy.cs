namespace HrService.Domain.Entities;

public class LeavePolicy
{
    public int Id { get; set; }
    public int LeaveTypeId { get; set; }
    public LeaveType? LeaveType { get; set; }
    public int Year { get; set; }
    public decimal DaysPerYear { get; set; }

    // Ngày bắt đầu áp dụng của phiên bản chính sách này (giờ nghiệp vụ).
    // Mỗi thay đổi là một phiên bản mới; phiên bản cũ không bị sửa.
    public DateOnly EffectiveFrom { get; set; }
}