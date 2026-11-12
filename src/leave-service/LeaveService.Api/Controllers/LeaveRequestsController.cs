using System.Security.Claims;
using LeaveService.Api.Authorization;
using LeaveService.Application.Dtos;
using LeaveService.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LeaveService.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/leave-requests")]
public sealed class LeaveRequestsController(LeaveRequestService leaveRequestService) : ControllerBase
{
    [HttpGet]
    [Authorize(Policy = LeavePermissionPolicies.LeavesRead)]
    public async Task<ActionResult<IReadOnlyList<LeaveRequestResponse>>> GetVisible(
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        CancellationToken cancellationToken)
    {
        var employeeId = GetCurrentEmployeeId();
        if (!employeeId.HasValue)
        {
            return Unauthorized();
        }

        var authorization = Request.Headers.Authorization.ToString();
        var access = LeaveAccess.FromClaims(User);

        // Không truyền page/pageSize: giữ nguyên hành vi cũ.
        if (page is null && pageSize is null)
        {
            var all = await leaveRequestService.GetVisibleAsync(
                employeeId.Value, authorization, access,
                calendarOnly: false, from: null, to: null, cancellationToken);
            return Ok(all);
        }

        var paged = await leaveRequestService.GetVisiblePagedAsync(
            employeeId.Value, authorization, access, page ?? 1, pageSize ?? 20, cancellationToken);
        Response.Headers["X-Total-Count"] = paged.TotalCount.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return Ok(paged.Items);
    }

    [HttpGet("calendar")]
    [Authorize(Policy = LeavePermissionPolicies.LeavesRead)]
    public async Task<ActionResult<IReadOnlyList<LeaveRequestResponse>>> GetCalendar(
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        CancellationToken cancellationToken)
    {
        var employeeId = GetCurrentEmployeeId();
        if (!employeeId.HasValue)
        {
            return Unauthorized();
        }

        var requests = await leaveRequestService.GetVisibleAsync(
            employeeId.Value,
            Request.Headers.Authorization.ToString(),
            LeaveAccess.FromClaims(User),
            calendarOnly: true,
            from,
            to,
            cancellationToken);
        return Ok(requests);
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = LeavePermissionPolicies.LeavesRead)]
    public async Task<ActionResult<LeaveRequestResponse>> GetById(
        Guid id,
        CancellationToken cancellationToken)
    {
        var employeeId = GetCurrentEmployeeId();
        if (!employeeId.HasValue)
        {
            return Unauthorized();
        }

        var request = await leaveRequestService.GetByIdAsync(
            employeeId.Value,
            id,
            cancellationToken);
        return Ok(request);
    }

    [HttpGet("mine")]
    [Authorize(Policy = LeavePermissionPolicies.LeavesRead)]
    public async Task<ActionResult<IReadOnlyList<LeaveRequestResponse>>> GetMine(
        CancellationToken cancellationToken)
    {
        var employeeId = GetCurrentEmployeeId();
        if (!employeeId.HasValue)
        {
            return Unauthorized();
        }

        var requests = await leaveRequestService.GetMineAsync(employeeId.Value, cancellationToken);
        return Ok(requests);
    }

    [HttpPost]
    [Authorize(Policy = LeavePermissionPolicies.LeavesCreate)]
    public async Task<ActionResult<LeaveRequestResponse>> Create(
        [FromBody] CreateLeaveRequest request,
        CancellationToken cancellationToken)
    {
        var employeeId = GetCurrentEmployeeId();
        var authorization = Request.Headers.Authorization.ToString();
        if (!employeeId.HasValue || string.IsNullOrWhiteSpace(authorization))
        {
            return Unauthorized();
        }

        var created = await leaveRequestService.CreateAsync(
            employeeId.Value,
            authorization,
            request,
            cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
    }

    [Authorize(Policy = LeavePermissionPolicies.LeavesDecide)]
    [HttpGet("pending-approval")]
    public async Task<ActionResult<IReadOnlyList<LeaveRequestResponse>>> GetPendingApproval(
        CancellationToken cancellationToken)
    {
        var actorId = GetCurrentEmployeeId();
        var authorization = Request.Headers.Authorization.ToString();
        if (!actorId.HasValue || string.IsNullOrWhiteSpace(authorization))
        {
            return Unauthorized();
        }

        var requests = await leaveRequestService.GetPendingAsync(
            actorId.Value,
            authorization,
            LeaveAccess.FromClaims(User),
            cancellationToken);
        return Ok(requests);
    }

    [Authorize(Policy = LeavePermissionPolicies.LeavesDecide)]
    [HttpPost("{id:guid}/approve")]
    public Task<ActionResult<LeaveRequestResponse>> Approve(
        Guid id,
        [FromBody] LeaveDecisionRequest request,
        CancellationToken cancellationToken) =>
        DecideAsync(id, request, approve: true, cancellationToken);

    [Authorize(Policy = LeavePermissionPolicies.LeavesDecide)]
    [HttpPost("{id:guid}/reject")]
    public Task<ActionResult<LeaveRequestResponse>> Reject(
        Guid id,
        [FromBody] LeaveDecisionRequest request,
        CancellationToken cancellationToken) =>
        DecideAsync(id, request, approve: false, cancellationToken);

    [HttpPost("{id:guid}/cancel")]
    [Authorize(Policy = LeavePermissionPolicies.LeavesCancel)]
    public async Task<ActionResult<LeaveRequestResponse>> Cancel(
        Guid id,
        [FromBody] CancelLeaveRequest request,
        CancellationToken cancellationToken)
    {
        var employeeId = GetCurrentEmployeeId();
        if (!employeeId.HasValue)
        {
            return Unauthorized();
        }

        var cancelled = await leaveRequestService.CancelAsync(
            employeeId.Value,
            id,
            request.Note,
            cancellationToken);
        return Ok(cancelled);
    }

    private async Task<ActionResult<LeaveRequestResponse>> DecideAsync(
        Guid id,
        LeaveDecisionRequest request,
        bool approve,
        CancellationToken cancellationToken)
    {
        var actorId = GetCurrentEmployeeId();
        var authorization = Request.Headers.Authorization.ToString();
        if (!actorId.HasValue || string.IsNullOrWhiteSpace(authorization))
        {
            return Unauthorized();
        }

        var updated = await leaveRequestService.DecideAsync(
            actorId.Value,
            authorization,
            id,
            approve,
            LeaveAccess.FromClaims(User),
            request.Note,
            cancellationToken);
        return Ok(updated);
    }

    private Guid? GetCurrentEmployeeId()
    {
        var employeeIdValue = User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue("sub");
        return Guid.TryParse(employeeIdValue, out var employeeId) ? employeeId : null;
    }
}
