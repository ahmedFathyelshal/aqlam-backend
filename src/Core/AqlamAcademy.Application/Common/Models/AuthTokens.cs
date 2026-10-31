namespace AqlamAcademy.Application.Common.Models;

public record AuthUser(
    Guid Id,
    string UserName,
    string? Email,
    string FullName,
    IReadOnlyList<string> Roles);

public record AuthTokens(
    string AccessToken,
    DateTime AccessTokenExpiration,
    string RefreshToken,
    DateTime RefreshTokenExpiration,
    AuthUser User);
