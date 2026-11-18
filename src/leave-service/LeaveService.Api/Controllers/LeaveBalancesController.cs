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

        var balances = await leaveBalanceService.SynchronizeMineAsync(
            employeeId,
            year,
            authorization,
            cancellationToken);
        return Ok(balances);
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
