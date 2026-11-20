using HrService.Application.Abstractions;
using HrService.Application.Common;

namespace HrService.Application.Services;

// Chỉ xóa khi: (1) phiên bản còn được phép xóa theo ngày hiệu lực, và
// (2) Leave xác nhận không có đơn nào tham chiếu. Leave không trả lời thì không xóa.
// Giữa bước kiểm tra và bước xóa có một khoảng rất ngắn; phiên bản chưa hiệu lực
// nên khả năng có đơn mới tham chiếu trong khoảng đó là rất thấp và được ghi nhận.
public sealed class LeavePolicyVersionDeletionService(
    LeaveCatalogService catalogService,
    ILeavePolicyUsageClient usageClient)
{
    public async Task DeleteAsync(
        int id,
        string authorization,
        CancellationToken cancellationToken = default)
    {
        await catalogService.EnsureVersionDeletableAsync(id, cancellationToken);

        var referenced = await usageClient.CountReferencesAsync(id, authorization, cancellationToken);
        if (referenced > 0)
        {
            throw new ConflictException(
                $"Policy version {id} is referenced by {referenced} leave allocation(s) and cannot be deleted.");
        }

        await catalogService.DeleteLeavePolicyVersionAsync(id, cancellationToken);
    }
}
