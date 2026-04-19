using AqlamAcademy.Domain.Enums;

namespace AqlamAcademy.Api.Contracts.Auth;

public record RegisterRequest(
    string UserName,
    string Email,
    string Password,
    string FullName,
    UserType UserType);
