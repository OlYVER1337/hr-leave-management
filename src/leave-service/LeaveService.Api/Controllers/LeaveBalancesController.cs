using System.Security.Claims;
using LeaveService.Api.Authorization;
using LeaveService.Application.Dtos;
using LeaveService.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LeaveService.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/leave-balances")]
public sealed class LeaveBalancesController(
    LeaveBalanceService leaveBalanceService) : ControllerBase
{
    [HttpGet("mine")]
    [Authorize(Policy = LeavePermissionPolicies.LeavesReadSelfOrAll)]
    public async Task<ActionResult<IReadOnlyList<LeaveBalanceResponse>>> GetMine(
        [FromQuery] int? year,
        CancellationToken cancellationToken)
    {
        var employeeIdValue = User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue("sub");
        var authorization = Request.Headers["Authorization"].ToString();
        if (!Guid.TryParse(employeeIdValue, out var employeeId) ||
            string.IsNullOrWhiteSpace(authorization))
        {
            return Unauthorized();
        }

        var result = await leaveBalanceService.SynchronizeMineWithMissingPoliciesAsync(
            employeeId,
            year,
            authorization,
            cancellationToken);
        if (result.MissingPolicyLeaveTypes.Count > 0)
        {
            Response.Headers["X-Leave-Policy-Missing"] = string.Join(",", result.MissingPolicyLeaveTypes);
        }

        return Ok(result.Balances);
    }

    [HttpPost("mine/synchronize")]
    [Authorize(Policy = LeavePermissionPolicies.LeavesReadSelfOrAll)]
    public async Task<ActionResult<IReadOnlyList<LeaveBalanceResponse>>> SynchronizeMine(
        [FromQuery] int? year,
        CancellationToken cancellationToken)
    {
        var employeeIdValue = User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue("sub");
        if (!Guid.TryParse(employeeIdValue, out var employeeId))
        {
            return Unauthorized();
        }

        var authorization = Request.Headers["Authorization"].ToString();
        if (string.IsNullOrWhiteSpace(authorization))
        {
            return Unauthorized();
        }

        var balances = await leaveBalanceService.SynchronizeMineAsync(
            employeeId,
            year,
            authorization,
            cancellationToken);
        return Ok(balances);
    }
}
