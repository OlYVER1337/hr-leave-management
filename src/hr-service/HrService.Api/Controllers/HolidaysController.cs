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
        CancellationToken cancellationToken)
    {
        var holidays = await catalogService.GetHolidaysAsync(from, to, cancellationToken);
        return Ok(holidays);
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<HolidayResponse>> GetById(
        int id,
        CancellationToken cancellationToken)
    {
        var holiday = await catalogService.GetHolidayByIdAsync(id, cancellationToken);
        return Ok(holiday);
    }

    [Authorize(Roles = "HR")]
    [HttpPost]
    public async Task<ActionResult<HolidayResponse>> Create(
        [FromBody] HolidayRequest request,
        CancellationToken cancellationToken)
    {
        var holiday = await catalogService.CreateHolidayAsync(request, cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id = holiday.Id }, holiday);
    }

    [Authorize(Roles = "HR")]
    [HttpPut("{id:int}")]
    public async Task<ActionResult<HolidayResponse>> Update(
        int id,
        [FromBody] HolidayRequest request,
        CancellationToken cancellationToken)
    {
        var holiday = await catalogService.UpdateHolidayAsync(id, request, cancellationToken);
        return Ok(holiday);
    }
}
