namespace AqlamAcademy.Application.Features.Auth.Commands.Register;

public record RegisterResult(
    Guid Id,
    string Email,
    string Token);
