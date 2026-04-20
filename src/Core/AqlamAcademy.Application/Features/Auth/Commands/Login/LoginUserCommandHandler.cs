using MediatR;
using AqlamAcademy.Application.Common.Interfaces;
using AqlamAcademy.Application.Common.Models;

namespace AqlamAcademy.Application.Features.Auth.Commands.Login;

public class LoginUserCommandHandler : IRequestHandler<LoginUserCommand, Result<AuthTokens>>
{
    private readonly IIdentityService _identityService;

    public LoginUserCommandHandler(IIdentityService identityService)
    {
        _identityService = identityService;
    }

    public async Task<Result<AuthTokens>> Handle(LoginUserCommand request, CancellationToken cancellationToken)
    {
        var result = await _identityService.LoginAsync(request.UserName, request.Password);

        return result;
    }
}
