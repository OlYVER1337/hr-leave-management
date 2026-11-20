using HrService.Api.Authorization;
using HrService.Application.Dtos;
using HrService.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HrService.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/holidays")]
public sealed class HolidaysController(LeaveCatalogService catalogService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<HolidayResponse>>> GetAll(
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        [FromQuery] bool authoritative,
        CancellationToken cancellationToken)
    {
        var holidays = authoritative
            ? await catalogService.GetAuthoritativeHolidaysAsync(from, to, cancellationToken)
            : await catalogService.GetHolidaysAsync(from, to, cancellationToken);
        return Ok(holidays);
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<HolidayResponse>> GetById(
        int id,
        [FromQuery] bool authoritative,
        CancellationToken cancellationToken)
    {
        var holiday = authoritative
            ? await catalogService.GetAuthoritativeHolidayByIdAsync(id, cancellationToken)
            : await catalogService.GetHolidayByIdAsync(id, cancellationToken);
        return Ok(holiday);
    }

    [Authorize(Policy = PermissionPolicies.HolidayCreate)]
    [HttpPost]
    public async Task<ActionResult<HolidayResponse>> Create(
        [FromBody] HolidayRequest request,
        CancellationToken cancellationToken)
    {
        var holiday = await catalogService.CreateHolidayAsync(request, cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id = holiday.Id }, holiday);
    }

    [Authorize(Policy = PermissionPolicies.HolidayUpdate)]
    [HttpPut("{id:int}")]
    public async Task<ActionResult<HolidayResponse>> Update(
        int id,
        [FromBody] HolidayRequest request,
        CancellationToken cancellationToken)
    {
        var holiday = await catalogService.UpdateHolidayAsync(id, request, cancellationToken);
        return Ok(holiday);
    }

    [Authorize(Policy = PermissionPolicies.HolidayDelete)]
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        await catalogService.DeleteHolidayAsync(id, cancellationToken);
        return NoContent();
    }
}
