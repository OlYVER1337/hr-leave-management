using System.Security.Claims;
using LeaveService.Application.Dtos;
using LeaveService.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LeaveService.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/leave-balances")]
public sealed class LeaveBalancesController(LeaveRequestService leaveRequestService) : ControllerBase
{
    [HttpGet("mine")]
    public async Task<ActionResult<IReadOnlyList<LeaveBalanceResponse>>> GetMine(
        [FromQuery] int? year,
        CancellationToken cancellationToken)
    {
        var employeeIdValue = User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue("sub");
        if (!Guid.TryParse(employeeIdValue, out var employeeId))
        {
            return Unauthorized();
        }

        var balances = await leaveRequestService.GetBalancesAsync(
            employeeId,
            year ?? DateTime.UtcNow.Year,
            cancellationToken);
        return Ok(balances);
    }
}
