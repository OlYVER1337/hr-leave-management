using System.ComponentModel.DataAnnotations;
using HrService.Api.Authorization;
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
    [Authorize(Policy = PermissionPolicies.EmployeesReadTeam)]
    public async Task<ActionResult<IReadOnlyList<EmployeeResponse>>> GetTeam(
        [FromQuery, Range(1, int.MaxValue)] int? page,
        [FromQuery, Range(1, 100)] int? pageSize,
        [FromQuery] string? search = null,
        CancellationToken cancellationToken = default)
    {
        if (!IsValidPagination(page, pageSize))
        {
            return BadRequest(new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Invalid pagination",
                Detail = "Both page and pageSize are required. page must be at least 1 and pageSize must be between 1 and 100."
            });
        }

        var employeeId = GetCurrentEmployeeId();
        if (!employeeId.HasValue)
        {
            return Unauthorized();
        }

        var paged = await employeeService.GetTeamPagedAsync(
            employeeId.Value, page!.Value, pageSize!.Value, search, cancellationToken);
        Response.Headers["X-Total-Count"] = paged.TotalCount.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return Ok(paged.Items);
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

    private static bool IsValidPagination(int? page, int? pageSize) =>
        page.HasValue && page.Value >= 1 &&
        pageSize.HasValue && pageSize.Value is >= 1 and <= 100;
}
