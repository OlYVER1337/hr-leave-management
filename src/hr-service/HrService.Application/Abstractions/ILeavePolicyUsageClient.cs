namespace HrService.Application.Abstractions;

// Hỏi Leave Service số phân bổ đơn đang tham chiếu tới một phiên bản chính sách.
public interface ILeavePolicyUsageClient
{
    Task<int> CountReferencesAsync(
        int policyVersionId,
        string authorization,
        CancellationToken cancellationToken = default);
}
