using AqlamAcademy.Application.Common.Models;
using AqlamAcademy.Infrastructure.Persistence.Identity;

namespace AqlamAcademy.Infrastructure.Persistence.Services;

public interface ITokenService
{
    Task<AuthTokens> GenerateAccessAndRefreshTokensAsync(
        ApplicationUser user, 
        IList<string> roles, 
        string? ipAddress, 
        string? userAgent,
        string? deviceId);

    Task<Result<AuthTokens>> RefreshSessionAsync(
        string refreshToken, 
        string? ipAddress, 
        string? userAgent,
        string? deviceId);

    Task<Result<bool>> RevokeTokenAsync(string refreshToken, string? ipAddress, string? deviceId);
}
