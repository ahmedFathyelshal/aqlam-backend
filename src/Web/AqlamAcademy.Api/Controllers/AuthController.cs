using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using MediatR;
using MapsterMapper;
using AqlamAcademy.Api.Contracts.Auth;
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
    private readonly ISender _mediator;
    private readonly IMapper _mapper;
    private readonly ILogger<AuthController> _logger;

    public AuthController(ISender mediator, IMapper mapper, ILogger<AuthController> logger)
    {
        _mediator = mediator;
        _mapper = mapper;
        _logger = logger;
    }

    [HttpPost("register")]
    public async Task<ActionResult<Result<RegisterResponse>>> Register(RegisterRequest request)
    {
        _logger.LogInformation("Received register request for User: {UserName}, Email: {Email}", request.UserName, request.Email);

        var command = _mapper.Map<RegisterUserCommand>(request);
        var result = await _mediator.Send(command);
        
        if (!result.Succeeded)
        {
            _logger.LogWarning("Registration failed for User: {UserName}. Errors: {@Errors}", request.UserName, result.Errors);
            return BadRequest(result);
        }

        _logger.LogInformation("Registration successful for User: {UserName}", request.UserName);

        var response = _mapper.Map<RegisterResponse>(result.Data!);

        var finalResult = Result<RegisterResponse>.Success(response);

        return Ok(finalResult);
    }

    [HttpPost("login")]
    public async Task<ActionResult<Result<LoginResponse>>> Login(LoginRequest request)
    {
        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
        var userAgent = Request.Headers.UserAgent.ToString();

        _logger.LogInformation("Received login request for User: {UserName} from IP: {IpAddress}", request.UserName, ipAddress);

        var command = _mapper.Map<LoginUserCommand>(request) with 
        { 
            IpAddress = ipAddress, 
            UserAgent = userAgent,
            DeviceId = request.DeviceId
        };

        var result = await _mediator.Send(command);

        if (!result.Succeeded)
        {
            _logger.LogWarning("Login failed for User: {UserName} from IP: {IpAddress}. Errors: {@Errors}", request.UserName, ipAddress, result.Errors);
            return BadRequest(result);
        }

        _logger.LogInformation("Login successful for User: {UserName} from IP: {IpAddress}", request.UserName, ipAddress);

        var response = _mapper.Map<LoginResponse>(result.Data!);

        // Securely append the tokens inside HTTP-Only cookies, restricting XSS access.
        SetTokenCookie("accessToken", result.Data!.AccessToken, result.Data.AccessTokenExpiration);
        SetTokenCookie("refreshToken", result.Data!.RefreshToken, result.Data.RefreshTokenExpiration);

        var finalResult = Result<LoginResponse>.Success(response);

        return Ok(finalResult);
    }

    [HttpPost("refresh")]
    public async Task<ActionResult<Result<LoginResponse>>> Refresh([FromBody] RefreshRequest request)
    {
        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
        _logger.LogInformation("Received refresh session request from IP: {IpAddress}", ipAddress);

        var refreshToken = Request.Cookies["refreshToken"];
        
        if (string.IsNullOrEmpty(refreshToken))
        {
            _logger.LogWarning("Refresh attempted but no token cookie found for IP: {IpAddress}", ipAddress);
            return BadRequest(Result<LoginResponse>.Failure(new[] { "Refresh token is missing from cookies." }));
        }

        var userAgent = Request.Headers.UserAgent.ToString();

        var command = new RefreshSessionCommand(
            refreshToken, 
            ipAddress, 
            userAgent, 
            request.DeviceId);

        var result = await _mediator.Send(command);

        if (!result.Succeeded)
        {
            _logger.LogWarning("Refresh session failed. IP: {IpAddress}. Errors: {@Errors}", ipAddress, result.Errors);
            return BadRequest(result);
        }

        _logger.LogInformation("Refresh session successful. IP: {IpAddress}", ipAddress);

        var response = _mapper.Map<LoginResponse>(result.Data!);

        // Securely rotate the tokens inside HTTP-Only cookies
        SetTokenCookie("accessToken", result.Data!.AccessToken, result.Data.AccessTokenExpiration);
        SetTokenCookie("refreshToken", result.Data!.RefreshToken, result.Data.RefreshTokenExpiration);

        var finalResult = Result<LoginResponse>.Success(response);

        return Ok(finalResult);
    }

    [HttpPost("logout")]
    public async Task<ActionResult<Result<bool>>> Logout([FromBody] LogoutRequest request)
    {
        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
        _logger.LogInformation("Received logout request from IP: {IpAddress}, DeviceId: {DeviceId}", ipAddress, request.DeviceId);

        var refreshToken = Request.Cookies["refreshToken"];
        
        if (string.IsNullOrEmpty(refreshToken))
        {
            _logger.LogInformation("Logout attempted but no token cookie found for IP: {IpAddress}", ipAddress);
            // We consider this a success from the client's perspective as they are already "logged out"
            return Ok(Result<bool>.Success(true));
        }

        var command = new LogoutUserCommand(
            refreshToken, 
            ipAddress,
            request.DeviceId);

        var result = await _mediator.Send(command);

        if (!result.Succeeded)
        {
            _logger.LogWarning("Logout session failed. IP: {IpAddress}. Errors: {@Errors}", ipAddress, result.Errors);
            // Even if revocation fails on DB side, clear the cookies
            Response.Cookies.Delete("accessToken");
            Response.Cookies.Delete("refreshToken");
            return BadRequest(result);
        }

        _logger.LogInformation("Logout session successful. IP: {IpAddress}", ipAddress);

        // Delete the cookies
        Response.Cookies.Delete("accessToken");
        Response.Cookies.Delete("refreshToken");

        var finalResult = Result<bool>.Success(true);

        return Ok(finalResult);
    }

    private void SetTokenCookie(string cookieName, string token, DateTime expires)
    {
        var isDevelopment = HttpContext.RequestServices
            .GetRequiredService<IWebHostEnvironment>()
            .IsDevelopment();

        var cookieOptions = new CookieOptions
        {
            HttpOnly = true,
            Expires = expires,
            Secure = !isDevelopment,
            SameSite = SameSiteMode.Strict,
            Path = "/"
        };
        Response.Cookies.Append(cookieName, token, cookieOptions);
    }

    [HttpGet("me")]
    [Microsoft.AspNetCore.Authorization.Authorize]
    public IActionResult GetCurrentUser()
    {
        var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? 
                     User.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub)?.Value;
        
        var email = User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value ?? 
                    User.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Email)?.Value;

        var name = User.Identity?.Name ?? 
                   User.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Name)?.Value;

        var roles = User.FindAll(System.Security.Claims.ClaimTypes.Role).Select(r => r.Value).ToList();

        // Returning a structure that the frontend expects to hydrate the user profile
        return Ok(Result<object>.Success(new
        {
            id = userId,
            name = name ?? "User",
            email = email,
            roles = roles,
            avatar = "assets/images/avatars/brian-hughes.jpg", // Mock avatar for now
            status = "online"
        }));
    }
}