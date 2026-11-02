namespace AqlamAcademy.Application.Common.Models;

public record AuthUser(
    Guid Id,
    string UserName,
    string? Email,
    string FullName,
    IReadOnlyList<string> Roles,
    bool MustChangePassword = false);

public record AuthTokens(
    string AccessToken,
    DateTime AccessTokenExpiration,
    string RefreshToken,
    DateTime RefreshTokenExpiration,
    AuthUser User);
