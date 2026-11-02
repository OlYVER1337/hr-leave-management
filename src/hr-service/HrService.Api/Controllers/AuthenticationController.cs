using HrService.Application.Dtos;
using HrService.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HrService.Api.Controllers;

[ApiController]
[Route("api/v1/auth")]
public sealed class AuthenticationController(
    AuthenticationService authenticationService,
    IWebHostEnvironment environment) : ControllerBase
{
    private const string RefreshTokenCookieName = "hr_leave_refresh";

    [AllowAnonymous]
    [HttpPost("login")]
    public async Task<ActionResult<AuthenticationResponse>> Login(
        [FromBody] AuthenticationRequest request,
        CancellationToken cancellationToken)
    {
        var session = await authenticationService.AuthenticateAsync(
            request.Email,
            request.Password,
            cancellationToken);

        SetRefreshTokenCookie(session);
        return Ok(session.Response);
    }

    [AllowAnonymous]
    [HttpPost("refresh")]
    public async Task<ActionResult<AuthenticationResponse>> Refresh(
        CancellationToken cancellationToken)
    {
        var session = await authenticationService.RefreshAsync(
            Request.Cookies[RefreshTokenCookieName],
            cancellationToken);
        SetRefreshTokenCookie(session);
        return Ok(session.Response);
    }

    [AllowAnonymous]
    [HttpPost("logout")]
    public async Task<IActionResult> Logout(CancellationToken cancellationToken)
    {
        await authenticationService.RevokeAsync(
            Request.Cookies[RefreshTokenCookieName],
            cancellationToken);
        ClearRefreshTokenCookie();
        return NoContent();
    }

    private void SetRefreshTokenCookie(AuthenticationSession session)
    {
        Response.Cookies.Append(
            RefreshTokenCookieName,
            session.RefreshToken,
            new CookieOptions
            {
                HttpOnly = true,
                Secure = !environment.IsDevelopment(),
                SameSite = SameSiteMode.Strict,
                IsEssential = true,
                Expires = session.RefreshTokenExpiresAtUtc,
                Path = "/"
            });
    }

    private void ClearRefreshTokenCookie()
    {
        Response.Cookies.Delete(
            RefreshTokenCookieName,
            new CookieOptions
            {
                HttpOnly = true,
                Secure = !environment.IsDevelopment(),
                SameSite = SameSiteMode.Strict,
                Path = "/"
            });
    }
}
