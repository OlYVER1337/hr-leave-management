using HrService.Application.Dtos;
using HrService.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace HrService.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/lookup/employees")]
public sealed class EmployeeLookupController(EmployeeService employeeService) : ControllerBase
{
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<EmployeeLookupResponse>> GetById(
        Guid id,
        CancellationToken cancellationToken)
    {
        var requesterId = GetRequesterId();
        if (!requesterId.HasValue)
        {
            return Unauthorized();
        }

        var employee = await employeeService.GetLookupAsync(
            id,
            requesterId.Value,
            User.IsInRole("HR"),
            cancellationToken);
        return Ok(employee);
    }

    [HttpGet("{managerId:guid}/team")]
    public async Task<ActionResult<IReadOnlyList<EmployeeLookupResponse>>> GetTeam(
        Guid managerId,
        CancellationToken cancellationToken)
    {
        var requesterId = GetRequesterId();
        if (!requesterId.HasValue)
        {
            return Unauthorized();
        }

        if (requesterId.Value != managerId && !User.IsInRole("HR"))
        {
            return Forbid();
        }

        var employees = await employeeService.GetTeamLookupAsync(managerId, cancellationToken);
        return Ok(employees);
    }

    private Guid? GetRequesterId()
    {
        var employeeIdValue = User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue("sub");
        return Guid.TryParse(employeeIdValue, out var employeeId)
            ? employeeId
            : null;
    }
}
