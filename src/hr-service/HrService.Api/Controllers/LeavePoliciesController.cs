using HrService.Api.Authorization;
using HrService.Application.Dtos;
using HrService.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HrService.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/leave-policies")]
public sealed class LeavePoliciesController(
    LeaveCatalogService catalogService,
    LeavePolicyVersionDeletionService deletionService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<LeavePolicyResponse>>> GetAll(
        [FromQuery] int? year,
        [FromQuery] DateOnly? asOfDate,
        [FromQuery] bool authoritative,
        CancellationToken cancellationToken)
    {
        var policies = authoritative || asOfDate.HasValue
            ? await catalogService.GetAuthoritativeLeavePoliciesAsync(year, asOfDate, cancellationToken)
            : await catalogService.GetLeavePoliciesAsync(year, cancellationToken);
        return Ok(policies);
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<LeavePolicyResponse>> GetById(
        int id,
        [FromQuery] bool authoritative,
        CancellationToken cancellationToken)
    {
        var policy = authoritative
            ? await catalogService.GetAuthoritativeLeavePolicyByIdAsync(id, cancellationToken)
            : await catalogService.GetLeavePolicyByIdAsync(id, cancellationToken);
        return Ok(policy);
    }

    [Authorize(Policy = PermissionPolicies.LeavePolicyCreate)]
    [HttpPost]
    public async Task<ActionResult<LeavePolicyResponse>> Create(
        [FromBody] LeavePolicyRequest request,
        CancellationToken cancellationToken)
    {
        var policy = await catalogService.CreateLeavePolicyAsync(request, cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id = policy.Id }, policy);
    }

    [Authorize(Policy = PermissionPolicies.LeavePolicyUpdate)]
    [HttpPut("{id:int}")]
    public async Task<ActionResult<LeavePolicyResponse>> Update(
        int id,
        [FromBody] LeavePolicyRequest request,
        CancellationToken cancellationToken)
    {
        var policy = await catalogService.UpdateLeavePolicyAsync(id, request, cancellationToken);
        return Ok(policy);
    }

    [Authorize(Policy = PermissionPolicies.LeavePolicyUpdate)]
    [HttpPut("{id:int}/edit")]
    public async Task<ActionResult<LeavePolicyResponse>> EditVersion(
        int id,
        [FromBody] LeavePolicyRequest request,
        CancellationToken cancellationToken)
    {
        var policy = await catalogService.EditLeavePolicyVersionAsync(id, request, cancellationToken);
        return Ok(policy);
    }

    [Authorize(Policy = PermissionPolicies.LeavePolicyUpdate)]
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> DeleteVersion(
        int id,
        CancellationToken cancellationToken)
    {
        await deletionService.DeleteAsync(id, Request.Headers.Authorization.ToString(), cancellationToken);
        return NoContent();
    }
}
