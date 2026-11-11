using HrService.Api.Authorization;
using HrService.Application.Dtos;
using HrService.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HrService.Api.Controllers;

[ApiController]
[Authorize(Policy = PermissionPolicies.RolesManage)]
[Route("api/v1")]
public sealed class EmployeeRolesController(UserRoleService userRoleService) : ControllerBase
{
    [HttpGet("roles")]
    public async Task<ActionResult<IReadOnlyList<RoleOptionResponse>>> GetAvailableRoles(
        CancellationToken cancellationToken)
    {
        var actorId = AuthorizationClaimReader.GetEmployeeId(User);
        if (!actorId.HasValue)
            return Unauthorized();

        var roles = await userRoleService.GetAvailableRolesAsync(actorId.Value, cancellationToken);
        return Ok(roles);
    }

    [HttpGet("employees/{employeeId:guid}/roles")]
    public async Task<ActionResult<IReadOnlyList<UserRoleAssignmentResponse>>> GetEmployeeRoles(
        Guid employeeId,
        CancellationToken cancellationToken)
    {
        var actorId = AuthorizationClaimReader.GetEmployeeId(User);
        if (!actorId.HasValue)
            return Unauthorized();

        var roles = await userRoleService.GetEmployeeRolesAsync(
            actorId.Value, employeeId, cancellationToken);
        return Ok(roles);
    }

    [HttpPost("employees/{employeeId:guid}/roles")]
    public async Task<ActionResult<UserRoleAssignmentResponse>> Grant(
        Guid employeeId,
        [FromBody] GrantUserRoleRequest request,
        CancellationToken cancellationToken)
    {
        var actorId = AuthorizationClaimReader.GetEmployeeId(User);
        if (!actorId.HasValue)
            return Unauthorized();

        var assignment = await userRoleService.GrantAsync(
            actorId.Value, employeeId, request, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, assignment);
    }

    [HttpDelete("employees/{employeeId:guid}/roles/{roleCode}")]
    public async Task<IActionResult> Revoke(
        Guid employeeId,
        string roleCode,
        CancellationToken cancellationToken)
    {
        var actorId = AuthorizationClaimReader.GetEmployeeId(User);
        if (!actorId.HasValue)
            return Unauthorized();

        await userRoleService.RevokeAsync(
            actorId.Value, employeeId, roleCode, cancellationToken);
        return NoContent();
    }
}
