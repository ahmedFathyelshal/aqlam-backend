using AqlamAcademy.Application.Common.Models;
using AqlamAcademy.Domain.Enums;

namespace AqlamAcademy.Application.Common.Interfaces;

public interface IIdentityService
{
    // Registration
    Task<Result<Guid>> CreateUserAsync(
        string userName,
        string email,
        string password, 
        UserType userType);
        
    Task<bool> IsUserNameUniqueAsync(string userName);

    // Login
    Task<Result<AuthTokens>> LoginAsync(string userName, string password, string? ipAddress, string? userAgent, string? deviceId, string? hostTenantId);
    
    // Refresh Token
    Task<Result<AuthTokens>> RefreshSessionAsync(string refreshToken, string? ipAddress, string? userAgent, string? deviceId, string? hostTenantId);

    // Password
    Task<Result<bool>> ChangePasswordAsync(Guid userId, string currentPassword, string newPassword);

    // Logout
    Task<Result<bool>> LogoutAsync(string refreshToken, string? ipAddress, string? deviceId);
}
