namespace HrService.Domain.Rules;

// Chính sách có ngày hiệu lực. Mỗi thay đổi là một phiên bản mới; phiên bản cũ không bị sửa.
public readonly record struct PolicyVersion(DateOnly EffectiveFrom, decimal DaysPerYear);

public static class LeavePolicyTimeline
{
    // Phiên bản đang có hiệu lực vào ngày `onDate`: EffectiveFrom <= onDate và gần nhất.
    public static PolicyVersion? EffectiveOn(IEnumerable<PolicyVersion> versions, DateOnly onDate)
    {
        PolicyVersion? current = null;
        foreach (var version in versions)
        {
            if (version.EffectiveFrom > onDate)
            {
                continue;
            }

            if (current is null || version.EffectiveFrom > current.Value.EffectiveFrom)
            {
                current = version;
            }
        }

        return current;
    }

    // Chỉ thêm phiên bản có hiệu lực từ hôm nay trở đi; không sửa lịch sử.
    public static bool CanAddVersion(DateOnly effectiveFrom, DateOnly today, int policyYear) =>
        effectiveFrom >= today && effectiveFrom.Year <= policyYear;

    // Hai phiên bản của cùng một năm không được cùng ngày hiệu lực.
    public static bool HasSameEffectiveDate(IEnumerable<PolicyVersion> versions, DateOnly effectiveFrom) =>
        versions.Any(version => version.EffectiveFrom == effectiveFrom);
}
