using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using MediatR;
using MapsterMapper;
using AqlamAcademy.Api.Contracts.Auth;
using AqlamAcademy.Api.Infrastructure;
using AqlamAcademy.Api.Infrastructure.DependencyInjection;
using AqlamAcademy.Application.Features.Auth.Commands.Register;
using AqlamAcademy.Application.Features.Auth.Commands.Login;
using AqlamAcademy.Application.Features.Auth.Commands.Refresh;
using AqlamAcademy.Application.Common.Models;
using AqlamAcademy.Application.Features.Auth.Commands.Logout;

namespace AqlamAcademy.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private const string AccessTokenCookie = "accessToken";
    private const string RefreshTokenCookie = "refreshToken";
    private const string DeviceIdCookie = "deviceId";

    private readonly ISender _mediator;
    private readonly IMapper _mapper;
    private readonly ILogger<AuthController> _logger;
    private readonly IWebHostEnvironment _environment;

    public AuthController(ISender mediator, IMapper mapper, ILogger<AuthController> logger, IWebHostEnvironment environment)
    {
        _mediator = mediator;
        _mapper = mapper;
        _logger = logger;
        _environment = environment;
    }

    [HttpPost("register")]
    [EnableRateLimiting(PresentationDependencyInjection.AuthRateLimitPolicy)]
    public async Task<ActionResult<Result<RegisterResponse>>> Register(RegisterRequest request)
    {
        _logger.LogInformation("Received register request for User: {UserName}", request.UserName);

        var command = _mapper.Map<RegisterUserCommand>(request);
        var result = await _mediator.Send(command);

        if (!result.Succeeded)
        {
            _logger.LogWarning("Registration failed for User: {UserName}. Errors: {@Errors}", request.UserName, result.Errors);
            return this.ToFailureResult(result);
        }

        _logger.LogInformation("Registration successful for User: {UserName}", request.UserName);

        var response = _mapper.Map<RegisterResponse>(result.Data!);

        return Ok(Result<RegisterResponse>.Success(response));
    }

    [HttpPost("login")]
    [EnableRateLimiting(PresentationDependencyInjection.AuthRateLimitPolicy)]
    public async Task<ActionResult<Result<LoginResponse>>> Login(LoginRequest request)
    {
        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
        var userAgent = Request.Headers.UserAgent.ToString();
        var deviceId = GetOrCreateDeviceId();

        _logger.LogInformation("Received login request for User: {UserName} from IP: {IpAddress}", request.UserName, ipAddress);

        var command = new LoginUserCommand(request.UserName, request.Password, ipAddress, userAgent, deviceId);

        var result = await _mediator.Send(command);

        if (!result.Succeeded)
        {
            _logger.LogWarning("Login failed for User: {UserName} from IP: {IpAddress}. Errors: {@Errors}", request.UserName, ipAddress, result.Errors);
            return this.ToFailureResult(result);
        }

        _logger.LogInformation("Login successful for User: {UserName} from IP: {IpAddress}", request.UserName, ipAddress);

        // Securely append the tokens inside HTTP-Only cookies, restricting XSS access.
        SetTokenCookies(result.Data!);

        return Ok(Result<LoginResponse>.Success(ToResponse(result.Data!.User)));
    }

    [HttpPost("refresh")]
    public async Task<ActionResult<Result<LoginResponse>>> Refresh()
    {
        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
        _logger.LogInformation("Received refresh session request from IP: {IpAddress}", ipAddress);

        var refreshToken = Request.Cookies[RefreshTokenCookie];

        if (string.IsNullOrEmpty(refreshToken))
        {
            _logger.LogWarning("Refresh attempted but no token cookie found for IP: {IpAddress}", ipAddress);
            return Unauthorized(Result<LoginResponse>.Unauthorized("Refresh token is missing from cookies."));
        }

        var userAgent = Request.Headers.UserAgent.ToString();

        var command = new RefreshSessionCommand(
            refreshToken,
            ipAddress,
            userAgent,
            GetOrCreateDeviceId());

        var result = await _mediator.Send(command);

        if (!result.Succeeded)
        {
            _logger.LogWarning("Refresh session failed. IP: {IpAddress}. Errors: {@Errors}", ipAddress, result.Errors);

            // A concurrent refresh (409) leaves the session intact; any other failure ends it.
            if (result.ErrorType != ResultErrorType.Conflict)
                DeleteTokenCookies();

            return this.ToFailureResult(result);
        }

        _logger.LogInformation("Refresh session successful. IP: {IpAddress}", ipAddress);

        // Securely rotate the tokens inside HTTP-Only cookies
        SetTokenCookies(result.Data!);

        return Ok(Result<LoginResponse>.Success(ToResponse(result.Data!.User)));
    }

    [HttpPost("logout")]
    public async Task<ActionResult<Result<bool>>> Logout()
    {
        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
        var deviceId = Request.Cookies[DeviceIdCookie];
        _logger.LogInformation("Received logout request from IP: {IpAddress}, DeviceId: {DeviceId}", ipAddress, deviceId);

        var refreshToken = Request.Cookies[RefreshTokenCookie];

        if (string.IsNullOrEmpty(refreshToken))
        {
            _logger.LogInformation("Logout attempted but no token cookie found for IP: {IpAddress}", ipAddress);
            // We consider this a success from the client's perspective as they are already "logged out"
            DeleteTokenCookies();
            return Ok(Result<bool>.Success(true));
        }

        var command = new LogoutUserCommand(refreshToken, ipAddress, deviceId);

        var result = await _mediator.Send(command);

        // Even if revocation fails on DB side, clear the cookies
        DeleteTokenCookies();

        if (!result.Succeeded)
        {
            _logger.LogWarning("Logout session failed. IP: {IpAddress}. Errors: {@Errors}", ipAddress, result.Errors);
            return this.ToFailureResult(result);
        }

        _logger.LogInformation("Logout session successful. IP: {IpAddress}", ipAddress);

        return Ok(Result<bool>.Success(true));
    }

    [HttpGet("me")]
    [Authorize]
    public ActionResult<Result<LoginResponse>> GetCurrentUser()
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ??
                     User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;

        var email = User.FindFirst(ClaimTypes.Email)?.Value ??
                    User.FindFirst(JwtRegisteredClaimNames.Email)?.Value;

        var userName = User.Identity?.Name ??
                       User.FindFirst(JwtRegisteredClaimNames.Name)?.Value ?? string.Empty;

        var roles = User.FindAll(ClaimTypes.Role).Select(r => r.Value).ToList();

        return Ok(Result<LoginResponse>.Success(new LoginResponse
        {
            Id = Guid.TryParse(userId, out var id) ? id : Guid.Empty,
            UserName = userName,
            Name = userName,
            Email = email,
            Roles = roles
        }));
    }

    private static LoginResponse ToResponse(AuthUser user) => new()
    {
        Id = user.Id,
        UserName = user.UserName,
        Name = string.IsNullOrWhiteSpace(user.FullName) ? user.UserName : user.FullName,
        Email = user.Email,
        Roles = user.Roles
    };

    /// <summary>
    /// The device id is issued by the server in a long-lived HttpOnly cookie, so the 2-device
    /// limit works without trusting a client-supplied id or browser fingerprinting.
    /// </summary>
    private string GetOrCreateDeviceId()
    {
        var deviceId = Request.Cookies[DeviceIdCookie];
        if (Guid.TryParse(deviceId, out _))
            return deviceId!;

        deviceId = Guid.NewGuid().ToString("N");
        Response.Cookies.Append(DeviceIdCookie, deviceId, BuildCookieOptions(DateTime.UtcNow.AddDays(400)));
        return deviceId;
    }

    private void SetTokenCookies(AuthTokens tokens)
    {
        Response.Cookies.Append(AccessTokenCookie, tokens.AccessToken, BuildCookieOptions(tokens.AccessTokenExpiration));
        Response.Cookies.Append(RefreshTokenCookie, tokens.RefreshToken, BuildCookieOptions(tokens.RefreshTokenExpiration));
    }

    private void DeleteTokenCookies()
    {
        Response.Cookies.Delete(AccessTokenCookie, BuildCookieOptions(null));
        Response.Cookies.Delete(RefreshTokenCookie, BuildCookieOptions(null));
    }

    private CookieOptions BuildCookieOptions(DateTime? expires) => new()
    {
        HttpOnly = true,
        Expires = expires,
        Secure = !_environment.IsDevelopment(),
        SameSite = SameSiteMode.Strict,
        Path = "/"
    };
}
