namespace AqlamAcademy.Api.Contracts.Auth;

public record RegisterResponse(
    Guid Id,
    string Email,
    string Token);
