using System.Security.Claims;
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
    [HttpGet("{id:guid}")]
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

    private Guid? GetCurrentEmployeeId()
    {
        var employeeIdValue = User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue("sub");
        return Guid.TryParse(employeeIdValue, out var employeeId) ? employeeId : null;
    }
}
