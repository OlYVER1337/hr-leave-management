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
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        CancellationToken cancellationToken)
    {
        var actorId = AuthorizationClaimReader.GetEmployeeId(User);
        if (!actorId.HasValue)
            return Unauthorized();

        var authorization = AuthorizationClaimReader.Read(User);

        // Không truyền page/pageSize: giữ nguyên hành vi cũ, trả toàn bộ danh sách trong phạm vi.
        if (page is null && pageSize is null)
        {
            return Ok(await employeeService.GetVisibleAsync(actorId.Value, authorization, cancellationToken));
        }

        var paged = await employeeService.GetVisiblePagedAsync(
            actorId.Value, authorization, page ?? 1, pageSize ?? 20, cancellationToken);
        Response.Headers["X-Total-Count"] = paged.TotalCount.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return Ok(paged.Items);
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
}
