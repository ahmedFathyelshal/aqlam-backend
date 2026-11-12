using AqlamAcademy.Domain.Enums;

namespace AqlamAcademy.Api.Contracts.Auth;

public record RegisterRequest(
    string UserName,
    string? Email, // optional: many students have none
    string Password,
    string ConfirmPassword,
    UserType UserType,
    int? BirthYear = null);
