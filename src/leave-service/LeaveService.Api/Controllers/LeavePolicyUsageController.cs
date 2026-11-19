using LeaveService.Api.Authorization;
using LeaveService.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LeaveService.Api.Controllers;

// Đọc nội bộ để HR kiểm tra trước khi xóa một phiên bản chính sách. Chỉ đọc, không ghi.
[ApiController]
[Authorize]
[Route("api/v1/leave-policy-usage")]
public sealed class LeavePolicyUsageController(LeaveRequestService leaveRequestService) : ControllerBase
{
    [HttpGet("{policyVersionId:int}")]
    [Authorize(Policy = LeavePermissionPolicies.LeavesRead)]
    public async Task<ActionResult<LeavePolicyUsageResponse>> Get(
        int policyVersionId,
        CancellationToken cancellationToken)
    {
        var count = await leaveRequestService.GetPolicyVersionReferenceCountAsync(
            policyVersionId,
            cancellationToken);
        return Ok(new LeavePolicyUsageResponse(policyVersionId, count));
    }
}

public sealed record LeavePolicyUsageResponse(int PolicyVersionId, int ReferencedAllocations);
