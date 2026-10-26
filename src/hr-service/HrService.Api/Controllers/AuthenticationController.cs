using HrService.Api.Dtos;
using HrService.Application.Dtos;
using HrService.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HrService.Api.Controllers;

[ApiController]
[Route("api/v1/auth")]
public sealed class AuthenticationController(AuthenticationService authenticationService) : ControllerBase
{
    [AllowAnonymous]
    [HttpPost("login")]
    public async Task<ActionResult<AuthenticationResponse>> Login(
        [FromBody] AuthenticationRequest request,
        CancellationToken cancellationToken)
    {
        var response = await authenticationService.AuthenticateAsync(
            request.Email,
            request.Password,
            cancellationToken);

        return Ok(response);
    }
}
