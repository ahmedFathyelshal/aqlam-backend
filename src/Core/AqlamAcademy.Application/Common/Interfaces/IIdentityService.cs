using AqlamAcademy.Application.Common.Models;
using AqlamAcademy.Domain.Enums;

namespace AqlamAcademy.Application.Common.Interfaces;

public interface IIdentityService
{
    // Registration
    /// <summary>Creates the user, role and profile. Birth year and full name are optional (students give a birth year).</summary>
    Task<Result<Guid>> CreateUserAsync(
        string userName,
        string email,
        string password, 
        UserType userType,
        int? birthYear = null,
        string? fullName = null);
        
    Task<bool> IsUserNameUniqueAsync(string userName);

    /// <summary>Sets the birth year of an account that has none. Once set, only support changes it.</summary>
    Task<Result> SetBirthYearOnceAsync(Guid userId, int birthYear);

    // Login
    Task<Result<AuthTokens>> LoginAsync(string userName, string password, string? ipAddress, string? userAgent, string? deviceId, string? hostTenantId);
    
    // Refresh Token
    Task<Result<AuthTokens>> RefreshSessionAsync(string refreshToken, string? ipAddress, string? userAgent, string? deviceId, string? hostTenantId);

    // Password
    Task<Result<bool>> ChangePasswordAsync(Guid userId, string currentPassword, string newPassword);

    // Logout
    Task<Result<bool>> LogoutAsync(string refreshToken, string? ipAddress, string? deviceId);
}
