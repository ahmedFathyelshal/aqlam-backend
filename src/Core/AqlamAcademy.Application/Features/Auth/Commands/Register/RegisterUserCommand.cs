using MediatR;
using AqlamAcademy.Application.Common.Interfaces;
using AqlamAcademy.Application.Common.Models;
using AqlamAcademy.Domain.Enums;

namespace AqlamAcademy.Application.Features.Auth.Commands.Register;

public record RegisterUserCommand : IRequest<Result<RegisterResponse>>
{
    public string UserName { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public string Password { get; init; } = string.Empty;
    public string FullName { get; init; } = string.Empty;
    public UserType UserType { get; init; }
}

public class RegisterUserCommandHandler : IRequestHandler<RegisterUserCommand, Result<RegisterResponse>>
{
    private readonly IIdentityService _identityService;

    public RegisterUserCommandHandler(IIdentityService identityService)
    {
        _identityService = identityService;
    }

    public async Task<Result<RegisterResponse>> Handle(RegisterUserCommand request, CancellationToken cancellationToken)
    {
        // 1. Create User and Profile via IdentityService
        var result = await _identityService.CreateUserAsync(
            request.UserName,
            request.Email,
            request.Password,
            request.FullName,
            request.UserType);

        if (!result.Succeeded)
        {
            return Result<RegisterResponse>.Failure(result.Errors);
        }

        // 2. Prepare Response (Token generation will be added in the next phase)
        var response = new RegisterResponse(
            result.Data,
            request.Email,
            request.FullName,
            "JWT_ACCESS_TOKEN_PLACEHOLDER");

        return Result<RegisterResponse>.Success(response);
    }
}
