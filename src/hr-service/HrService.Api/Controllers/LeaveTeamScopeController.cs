using HrService.Api.Authorization;
using HrService.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HrService.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/leave-scope")]
public sealed class LeaveTeamScopeController(EmployeeService employeeService) : ControllerBase
{
    [HttpGet("managers/{managerId:guid}/employee-ids")]
    public async Task<ActionResult<IReadOnlyList<Guid>>> GetTeamEmployeeIds(
        Guid managerId,
        CancellationToken cancellationToken)
    {
        var requesterId = AuthorizationClaimReader.GetEmployeeId(User);
        if (!requesterId.HasValue)
        {
            return Unauthorized();
        }

        if (requesterId.Value != managerId)
        {
            return Forbid();
        }

        var authorization = AuthorizationClaimReader.Read(User);
        var canReadLeaveTeam = authorization.HasScope("leaves.read", "Team");
        var canDecideLeaveTeam = authorization.HasScope("leaves.decide", "Team");
        if (!canReadLeaveTeam && !canDecideLeaveTeam)
        {
            return Forbid();
        }

        var employeeIds = await employeeService.GetTeamEmployeeIdsAsync(
            managerId,
            cancellationToken);
        return Ok(employeeIds);
    }
}
