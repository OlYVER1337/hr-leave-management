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

        var authorization = AuthorizationClaimReader.Read(User);

        var hasTeamScope =
            authorization.HasScope("leaves.read", "Team") ||
            authorization.HasScope("leaves.decide", "Team");

        var hasAllScope =
            authorization.HasScope("leaves.read", "All") ||
            authorization.HasScope("leaves.decide", "All");

        // Team chỉ được truy vấn team của chính người gọi.
        // All được phép truy vấn team của bất kỳ Manager nào.
        var isOwnTeam = requesterId.Value == managerId;
        if (!hasAllScope && !(isOwnTeam && hasTeamScope))
        {
            return Forbid();
        }

        var employeeIds = await employeeService.GetTeamEmployeeIdsAsync(
            managerId,
            cancellationToken);

        return Ok(employeeIds);
    }
}
