using Microsoft.AspNetCore.Mvc;
using MediatR;
using MapsterMapper;
using AqlamAcademy.Api.Contracts.Auth;
using AqlamAcademy.Application.Features.Auth.Commands.Register;
using AqlamAcademy.Application.Features.Auth.Commands.Login;
using AqlamAcademy.Application.Features.Auth.Commands.Refresh;
using AqlamAcademy.Application.Common.Models;

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

        // Securely append the Refresh Token inside the HTTP-Only cookie, restricting XSS access.
        SetTokenCookie(result.Data!.RefreshToken, result.Data.RefreshTokenExpiration);

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

        // Securely rotate the Refresh Token inside the HTTP-Only cookie
        SetTokenCookie(result.Data!.RefreshToken, result.Data.RefreshTokenExpiration);

        var finalResult = Result<LoginResponse>.Success(response);

        return Ok(finalResult);
    }

    private void SetTokenCookie(string token, DateTime expires)
    {
        var cookieOptions = new CookieOptions
        {
            HttpOnly = true,
            Expires = expires,
            Secure = true, // Must be true in production (requires HTTPS)
            SameSite = SameSiteMode.Strict
        };
        Response.Cookies.Append("refreshToken", token, cookieOptions);
    }
}