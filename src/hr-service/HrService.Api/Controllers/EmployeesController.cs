using System.ComponentModel.DataAnnotations;
using HrService.Api.Authorization;
using HrService.Application.Dtos;
using HrService.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HrService.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/employees")]
public sealed class EmployeesController(EmployeeService employeeService) : ControllerBase
{
    [HttpGet]
    [Authorize(Policy = PermissionPolicies.EmployeesRead)]
    public async Task<ActionResult<IReadOnlyList<EmployeeResponse>>> GetAll(
        [FromQuery, Range(1, int.MaxValue)] int? page,
        [FromQuery, Range(1, 100)] int? pageSize,
        [FromQuery] string? search = null,
        [FromQuery] string? role = null,
        [FromQuery] bool? isActive = null,
        [FromQuery] int? departmentId = null,
        [FromQuery] string? sortBy = "fullName",
        [FromQuery] string? sortDirection = "asc",
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

        var actorId = AuthorizationClaimReader.GetEmployeeId(User);
        if (!actorId.HasValue)
            return Unauthorized();

        var authorization = AuthorizationClaimReader.Read(User);

        var paged = await employeeService.GetVisiblePagedAsync(
            actorId.Value,
            authorization,
            page!.Value,
            pageSize!.Value,
            search,
            role,
            isActive,
            departmentId,
            sortBy,
            sortDirection,
            cancellationToken);
        Response.Headers["X-Total-Count"] = paged.TotalCount.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return Ok(paged.Items);
    }

    [HttpGet("overview")]
    [Authorize(Policy = PermissionPolicies.EmployeesRead)]
    public async Task<ActionResult<EmployeeOverviewResponse>> GetOverview(
        [FromQuery, Range(1, int.MaxValue)] int? page,
        [FromQuery, Range(1, 100)] int? pageSize,
        [FromQuery] string? search = null,
        [FromQuery] string? role = null,
        [FromQuery] bool? isActive = null,
        [FromQuery] int? departmentId = null,
        [FromQuery] string? sortBy = "fullName",
        [FromQuery] string? sortDirection = "asc",
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

        var actorId = AuthorizationClaimReader.GetEmployeeId(User);
        if (!actorId.HasValue)
            return Unauthorized();

        var overview = await employeeService.GetVisibleOverviewPagedAsync(
            actorId.Value,
            AuthorizationClaimReader.Read(User),
            page!.Value,
            pageSize!.Value,
            search,
            role,
            isActive,
            departmentId,
            sortBy,
            sortDirection,
            cancellationToken);

        Response.Headers["X-Total-Count"] = overview.TotalCount.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return Ok(overview);
    }

    [HttpGet("summary")]
    [Authorize(Policy = PermissionPolicies.EmployeesRead)]
    public async Task<ActionResult<EmployeeSummaryResponse>> GetSummary(
        CancellationToken cancellationToken)
    {
        var actorId = AuthorizationClaimReader.GetEmployeeId(User);
        if (!actorId.HasValue)
            return Unauthorized();

        var authorization = AuthorizationClaimReader.Read(User);
        var summary = await employeeService.GetVisibleSummaryAsync(
            actorId.Value, authorization, cancellationToken);
        return Ok(summary);
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = PermissionPolicies.EmployeesRead)]
    public async Task<ActionResult<EmployeeResponse>> GetById(
        Guid id,
        CancellationToken cancellationToken)
    {
        var actorId = AuthorizationClaimReader.GetEmployeeId(User);
        if (!actorId.HasValue)
            return Unauthorized();

        var authorization = AuthorizationClaimReader.Read(User);
        var employee = await employeeService.GetVisibleByIdAsync(
            id, actorId.Value, authorization, cancellationToken);
        return Ok(employee);
    }

    [HttpPost]
    [Authorize(Policy = PermissionPolicies.EmployeeCreate)]
    public async Task<ActionResult<EmployeeResponse>> Create(
        [FromBody] CreateEmployeeRequest request,
        CancellationToken cancellationToken)
    {
        var employee = await employeeService.CreateAsync(request, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, employee);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = PermissionPolicies.EmployeeUpdate)]
    public async Task<ActionResult<EmployeeResponse>> Update(
        Guid id,
        [FromBody] UpdateEmployeeRequest request,
        CancellationToken cancellationToken)
    {
        var employee = await employeeService.UpdateAsync(id, request, cancellationToken);
        return Ok(employee);
    }

    [HttpPost("{id:guid}/deactivate")]
    [Authorize(Policy = PermissionPolicies.EmployeeDeactivate)]
    public async Task<ActionResult<EmployeeResponse>> Deactivate(
        Guid id,
        CancellationToken cancellationToken)
    {
        var employee = await employeeService.DeactivateAsync(id, cancellationToken);
        return Ok(employee);
    }

    [HttpPut("{id:guid}/password")]
    [Authorize(Policy = PermissionPolicies.EmployeePasswordReset)]
    public async Task<IActionResult> ResetPassword(
        Guid id,
        [FromBody] ResetEmployeePasswordRequest request,
        CancellationToken cancellationToken)
    {
        await employeeService.ResetPasswordAsync(id, request, cancellationToken);
        return NoContent();
    }

    private static bool IsValidPagination(int? page, int? pageSize) =>
        page.HasValue && page.Value >= 1 &&
        pageSize.HasValue && pageSize.Value is >= 1 and <= 100;
}
