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
    Task<Result<AuthTokens>> LoginAsync(string userName, string password);
}
