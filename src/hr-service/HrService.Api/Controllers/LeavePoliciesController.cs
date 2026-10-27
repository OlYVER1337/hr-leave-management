using HrService.Application.Dtos;
using HrService.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HrService.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/leave-policies")]
public sealed class LeavePoliciesController(LeaveCatalogService catalogService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<LeavePolicyResponse>>> GetAll(
        [FromQuery] int? year,
        CancellationToken cancellationToken)
    {
        var policies = await catalogService.GetLeavePoliciesAsync(year, cancellationToken);
        return Ok(policies);
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<LeavePolicyResponse>> GetById(
        int id,
        CancellationToken cancellationToken)
    {
        var policy = await catalogService.GetLeavePolicyByIdAsync(id, cancellationToken);
        return Ok(policy);
    }

    [Authorize(Roles = "HR")]
    [HttpPost]
    public async Task<ActionResult<LeavePolicyResponse>> Create(
        [FromBody] LeavePolicyRequest request,
        CancellationToken cancellationToken)
    {
        var policy = await catalogService.CreateLeavePolicyAsync(request, cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id = policy.Id }, policy);
    }

    [Authorize(Roles = "HR")]
    [HttpPut("{id:int}")]
    public async Task<ActionResult<LeavePolicyResponse>> Update(
        int id,
        [FromBody] LeavePolicyRequest request,
        CancellationToken cancellationToken)
    {
        var policy = await catalogService.UpdateLeavePolicyAsync(id, request, cancellationToken);
        return Ok(policy);
    }
}
