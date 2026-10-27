using HrService.Application.Dtos;
using HrService.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HrService.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/leave-types")]
public sealed class LeaveTypesController(LeaveCatalogService catalogService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<LeaveTypeResponse>>> GetAll(
        [FromQuery] bool includeInactive,
        CancellationToken cancellationToken)
    {
        if (includeInactive && !User.IsInRole("HR"))
        {
            return Forbid();
        }

        var leaveTypes = await catalogService.GetLeaveTypesAsync(includeInactive, cancellationToken);
        return Ok(leaveTypes);
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<LeaveTypeResponse>> GetById(
        int id,
        CancellationToken cancellationToken)
    {
        var leaveType = await catalogService.GetLeaveTypeByIdAsync(id, cancellationToken);
        return Ok(leaveType);
    }

    [Authorize(Roles = "HR")]
    [HttpPost]
    public async Task<ActionResult<LeaveTypeResponse>> Create(
        [FromBody] CreateLeaveTypeRequest request,
        CancellationToken cancellationToken)
    {
        var leaveType = await catalogService.CreateLeaveTypeAsync(request, cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id = leaveType.Id }, leaveType);
    }

    [Authorize(Roles = "HR")]
    [HttpPut("{id:int}")]
    public async Task<ActionResult<LeaveTypeResponse>> Update(
        int id,
        [FromBody] UpdateLeaveTypeRequest request,
        CancellationToken cancellationToken)
    {
        var leaveType = await catalogService.UpdateLeaveTypeAsync(id, request, cancellationToken);
        return Ok(leaveType);
    }
}
