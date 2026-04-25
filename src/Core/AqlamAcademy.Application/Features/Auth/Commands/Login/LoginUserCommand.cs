using MediatR;
using AqlamAcademy.Application.Common.Models;
using AqlamAcademy.Application.Common.Interfaces;
using Microsoft.Extensions.Logging;

namespace AqlamAcademy.Application.Features.Auth.Commands.Login;

public record LoginUserCommand(
    string UserName, 
    string Password,
    string? IpAddress,
    string? UserAgent,
    string? DeviceId) : IRequest<Result<AuthTokens>>;

public class LoginUserCommandHandler : IRequestHandler<LoginUserCommand, Result<AuthTokens>>
{
    private readonly IIdentityService _identityService;
    private readonly ILogger<LoginUserCommandHandler> _logger;

    public LoginUserCommandHandler(IIdentityService identityService, ILogger<LoginUserCommandHandler> logger)
    {
        _identityService = identityService;
        _logger = logger;
    }

    public async Task<Result<AuthTokens>> Handle(LoginUserCommand request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Processing Login Command for {UserName}. DeviceId: {DeviceId}", request.UserName, request.DeviceId);
        var result = await _identityService.LoginAsync(
            request.UserName, 
            request.Password,
            request.IpAddress,
            request.UserAgent,
            request.DeviceId);

        return result;
    }
}
