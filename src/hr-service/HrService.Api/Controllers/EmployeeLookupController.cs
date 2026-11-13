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
[Route("api/v1/lookup/employees")]
public sealed class EmployeeLookupController(EmployeeService employeeService) : ControllerBase
{
    [HttpPost("batch")]
    [Authorize(Policy = PermissionPolicies.EmployeesRead)]
    public async Task<ActionResult<IReadOnlyList<EmployeeLookupResponse>>> GetBatch(
        [FromBody] Guid[] ids,
        CancellationToken cancellationToken)
    {
        var requesterId = GetRequesterId();
        if (!requesterId.HasValue)
            return Unauthorized();

        if (ids.Length > 100)
            return BadRequest(new { error = "At most 100 employee IDs can be requested at once." });

        var employees = await employeeService.GetLookupsAsync(
            ids,
            requesterId.Value,
            AuthorizationClaimReader.Read(User),
            cancellationToken);
        return Ok(employees);
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = PermissionPolicies.EmployeesRead)]
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
            AuthorizationClaimReader.Read(User),
            cancellationToken);
        return Ok(employee);
    }

    [HttpGet("{managerId:guid}/team")]
    [Authorize(Policy = PermissionPolicies.EmployeesReadTeamOrAll)]
    public async Task<ActionResult<IReadOnlyList<EmployeeLookupResponse>>> GetTeam(
        Guid managerId,
        [FromQuery, Range(1, int.MaxValue)] int page,
        [FromQuery, Range(1, 100)] int pageSize,
        [FromQuery] string? search = null,
        CancellationToken cancellationToken = default)
    {
        var requesterId = GetRequesterId();
        if (!requesterId.HasValue)
        {
            return Unauthorized();
        }

        var authorization = AuthorizationClaimReader.Read(User);
        var canReadAll = authorization.HasScope("employees.read", "All");
        var canReadOwnTeam = requesterId.Value == managerId &&
            authorization.HasScope("employees.read", "Team");
        if (!canReadAll && !canReadOwnTeam)
        {
            return Forbid();
        }

        var paged = await employeeService.GetTeamPagedAsync(
            managerId, page, pageSize, search, cancellationToken);
        Response.Headers["X-Total-Count"] = paged.TotalCount.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var employees = paged.Items.Select(employee => new EmployeeLookupResponse(
            employee.Id,
            employee.Code,
            employee.FullName,
            employee.Role,
            employee.IsActive,
            employee.DepartmentId,
            employee.ManagerId,
            employee.DepartmentName,
            employee.ManagerName));
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
