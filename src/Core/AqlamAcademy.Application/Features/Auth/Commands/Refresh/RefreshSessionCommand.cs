using MediatR;
using AqlamAcademy.Application.Common.Models;
using AqlamAcademy.Application.Common.Interfaces;
using Microsoft.Extensions.Logging;

namespace AqlamAcademy.Application.Features.Auth.Commands.Refresh;

public record RefreshSessionCommand(
    string RefreshToken,
    string? IpAddress,
    string? UserAgent,
    string? DeviceId,
    string? HostTenantId = null) : IRequest<Result<AuthTokens>>;

public class RefreshSessionCommandHandler : IRequestHandler<RefreshSessionCommand, Result<AuthTokens>>
{
    private readonly IIdentityService _identityService;
    private readonly ILogger<RefreshSessionCommandHandler> _logger;

    public RefreshSessionCommandHandler(IIdentityService identityService, ILogger<RefreshSessionCommandHandler> logger)
    {
        _identityService = identityService;
        _logger = logger;
    }

    public async Task<Result<AuthTokens>> Handle(RefreshSessionCommand request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Processing Refresh Session Command for DeviceId: {DeviceId}", request.DeviceId);
        return await _identityService.RefreshSessionAsync(
            request.RefreshToken,
            request.IpAddress,
            request.UserAgent,
            request.DeviceId,
            request.HostTenantId);
    }
}
