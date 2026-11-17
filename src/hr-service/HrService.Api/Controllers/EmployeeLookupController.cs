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


    private Guid? GetRequesterId()
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
