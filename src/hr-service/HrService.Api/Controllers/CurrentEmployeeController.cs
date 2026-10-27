using HrService.Application.Dtos;
using HrService.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace HrService.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/me")]
public sealed class CurrentEmployeeController(EmployeeService employeeService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<EmployeeResponse>> Get(
        CancellationToken cancellationToken)
    {
        var employeeId = GetCurrentEmployeeId();
        if (!employeeId.HasValue)
        {
            return Unauthorized();
        }

        var employee = await employeeService.GetByIdAsync(employeeId.Value, cancellationToken);
        return Ok(employee);
    }

    [HttpGet("team")]
    public async Task<ActionResult<IReadOnlyList<EmployeeResponse>>> GetTeam(
        CancellationToken cancellationToken)
    {
        var employeeId = GetCurrentEmployeeId();
        if (!employeeId.HasValue)
        {
            return Unauthorized();
        }

        var team = await employeeService.GetTeamAsync(employeeId.Value, cancellationToken);
        return Ok(team);
    }

    [HttpPut("password")]
    public async Task<IActionResult> ChangePassword(
        [FromBody] ChangeEmployeePasswordRequest request,
        CancellationToken cancellationToken)
    {
        var employeeId = GetCurrentEmployeeId();
        if (!employeeId.HasValue)
        {
            return Unauthorized();
        }

        await employeeService.ChangePasswordAsync(employeeId.Value, request, cancellationToken);
        return NoContent();
    }

    private Guid? GetCurrentEmployeeId()
    {
        var employeeIdValue = User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue("sub");
        return Guid.TryParse(employeeIdValue, out var employeeId)
            ? employeeId
            : null;
    }
}
