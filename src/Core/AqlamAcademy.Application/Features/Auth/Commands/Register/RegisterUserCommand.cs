using MediatR;
using AqlamAcademy.Application.Common.Interfaces;
using AqlamAcademy.Application.Common.Models;
using AqlamAcademy.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace AqlamAcademy.Application.Features.Auth.Commands.Register;

public record RegisterUserCommand : IRequest<Result<RegisterResult>>
{
    public string UserName { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public string Password { get; init; } = string.Empty;
    public string ConfirmPassword { get; init; } = string.Empty;
    public UserType UserType { get; init; }
}

public class RegisterUserCommandHandler : IRequestHandler<RegisterUserCommand, Result<RegisterResult>>
{
    private readonly IIdentityService _identityService;
    private readonly ILocalizationService _localizer;
    private readonly ILogger<RegisterUserCommandHandler> _logger;

    public RegisterUserCommandHandler(
        IIdentityService identityService, 
        ILocalizationService localizer,
        ILogger<RegisterUserCommandHandler> logger)
    {
        _identityService = identityService;
        _localizer = localizer;
        _logger = logger;
    }

    public async Task<Result<RegisterResult>> Handle(RegisterUserCommand request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Processing Register Command for Email: {Email}", request.Email);
        // 1. Create User and Profile via IdentityService
        var result = await _identityService.CreateUserAsync(
            request.UserName,
            request.Email,
            request.Password,
            request.UserType);

        if (!result.Succeeded)
        {
            return Result<RegisterResult>.Failure(result.Errors);
        }

        // 2. Prepare Response
        var response = new RegisterResult(_localizer["RegistrationSuccessful"]);

        return Result<RegisterResult>.Success(response);
    }
}
