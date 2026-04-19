namespace AqlamAcademy.Application.Features.Auth.Commands.Register;

public record RegisterResponse(
    Guid Id,
    string Email,
    string FullName,
    string Token);
