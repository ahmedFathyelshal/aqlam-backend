using MediatR;
using AqlamAcademy.Application.Common.Models;

namespace AqlamAcademy.Application.Features.Auth.Commands.Login;

public record LoginUserCommand(string UserName, string Password) : IRequest<Result<AuthTokens>>;
