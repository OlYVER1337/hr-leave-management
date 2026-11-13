using HrService.Api.Authorization;
using HrService.Application.Dtos;
using HrService.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HrService.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/leave-catalog")]
public sealed class LeaveCatalogController(LeaveCatalogService catalogService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<LeaveCatalogResponse>> Get(
        [FromQuery] int? year,
        [FromQuery] bool includeInactive,
        CancellationToken cancellationToken)
    {
        if (includeInactive &&
            !AuthorizationClaimReader.Read(User).HasScope("leave-types.update", "All"))
        {
            return Forbid();
        }

        var catalog = await catalogService.GetLeaveCatalogAsync(
            year,
            includeInactive,
            cancellationToken);
        return Ok(catalog);
    }
}
