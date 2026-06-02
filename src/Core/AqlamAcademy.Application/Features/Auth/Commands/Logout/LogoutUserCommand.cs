using MediatR;
using AqlamAcademy.Application.Common.Models;
using AqlamAcademy.Application.Common.Interfaces;
using Microsoft.Extensions.Logging;

namespace AqlamAcademy.Application.Features.Auth.Commands.Logout;

public record LogoutUserCommand(
    string RefreshToken,
    string? IpAddress,
    string? DeviceId) : IRequest<Result<bool>>;

public class LogoutUserCommandHandler : IRequestHandler<LogoutUserCommand, Result<bool>>
{
    private readonly IIdentityService _identityService;
    private readonly ILogger<LogoutUserCommandHandler> _logger;

    public LogoutUserCommandHandler(IIdentityService identityService, ILogger<LogoutUserCommandHandler> logger)
    {
        _identityService = identityService;
        _logger = logger;
    }

    public async Task<Result<bool>> Handle(LogoutUserCommand request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Processing Logout Command from IP: {IpAddress}, DeviceId: {DeviceId}", request.IpAddress, request.DeviceId);
        return await _identityService.LogoutAsync(
            request.RefreshToken,
            request.IpAddress,
            request.DeviceId);
    }
}
