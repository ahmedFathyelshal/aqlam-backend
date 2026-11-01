using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using AqlamAcademy.Application.Common.Interfaces;
using AqlamAcademy.Application.Common.Models;
using AqlamAcademy.Domain.Enums;
using AqlamAcademy.Domain.Students;
using AqlamAcademy.Domain.Staff;
using AqlamAcademy.Infrastructure.Persistence.Identity;

namespace AqlamAcademy.Infrastructure.Persistence.Services;

public class IdentityService : IIdentityService
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ApplicationDbContext _context;
    private readonly ILogger<IdentityService> _logger;
    private readonly ITokenService _tokenService;
    private readonly ILocalizationService _localizer;
    private readonly PersonalOrganizationFactory _personalOrganizations;

    public IdentityService(
        UserManager<ApplicationUser> userManager,
        ApplicationDbContext context,
        ILogger<IdentityService> logger,
        ITokenService tokenService,
        ILocalizationService localizer,
        PersonalOrganizationFactory personalOrganizations)
    {
        _userManager = userManager;
        _context = context;
        _logger = logger;
        _tokenService = tokenService;
        _localizer = localizer;
        _personalOrganizations = personalOrganizations;
    }

    public async Task<bool> IsUserNameUniqueAsync(string userName)
    {
        return await _userManager.FindByNameAsync(userName) == null;
    }

    public async Task<Result<AuthTokens>> LoginAsync(string userName, string password, string? ipAddress, string? userAgent, string? deviceId, string? hostTenantId)
    {
        var user = await _userManager.FindByNameAsync(userName);
        
        if (user == null)
            return Result<AuthTokens>.Failure(new[] { _localizer["InvalidCredentials"] });

        if (user.IsDeleted)
            return Result<AuthTokens>.Failure(new[] { _localizer["UserAccountDeleted"] });

        if (await _userManager.IsLockedOutAsync(user))
        {
            var lockoutEnd = user.LockoutEnd?.ToLocalTime().ToString("g") ?? "later";
            return Result<AuthTokens>.Failure(new[] { _localizer.GetLocalizedString("UserAccountLocked", lockoutEnd) });
        }

        var checkPasswordResult = await _userManager.CheckPasswordAsync(user, password);
        if (!checkPasswordResult)
        {
            await _userManager.AccessFailedAsync(user);
            return Result<AuthTokens>.Failure(new[] { _localizer["InvalidCredentials"] });
        }
        
        // Reset access failed count on successful login
        await _userManager.ResetAccessFailedCountAsync(user);

        var roles = await _userManager.GetRolesAsync(user);

        var tokens = await _tokenService.GenerateAccessAndRefreshTokensAsync(
            user, 
            roles, 
            ipAddress, 
            userAgent,
            deviceId,
            hostTenantId);

        return Result<AuthTokens>.Success(tokens);
    }

    public async Task<Result<AuthTokens>> RefreshSessionAsync(string refreshToken, string? ipAddress, string? userAgent, string? deviceId, string? hostTenantId)
    {
        return await _tokenService.RefreshSessionAsync(refreshToken, ipAddress, userAgent, deviceId, hostTenantId);
    }

    public async Task<Result<bool>> LogoutAsync(string refreshToken, string? ipAddress, string? deviceId)
    {
        return await _tokenService.RevokeTokenAsync(refreshToken, ipAddress, deviceId);
    }

    public async Task<Result<Guid>> CreateUserAsync(
        string userName, 
        string email,
        string password, 
        UserType userType)
    {
        using var transaction = await _context.Database.BeginTransactionAsync();

        try
        {
            var user = new ApplicationUser
            {
                UserName = userName,
                Email = email,
                FullName = string.Empty, // Default value as it's no longer collected at registration
                EmailConfirmed = true 
            };

            var createResult = await _userManager.CreateAsync(user, password);
            if (!createResult.Succeeded)
            {
                return Result<Guid>.Failure(createResult.Errors.Select(e => e.Description));
            }

            var roleResult = await AssignRoleAndProfileAsync(user, userType);
            if (!roleResult.Succeeded)
            {
                await transaction.RollbackAsync();
                return roleResult;
            }

            await transaction.CommitAsync();
            return Result<Guid>.Success(user.Id);
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            _logger.LogError(ex, "An error occurred during user registration for {Email}", email);
            return Result<Guid>.Failure(new[] { "An unexpected error occurred during registration." });
        }
    }


    private async Task<Result<Guid>> AssignRoleAndProfileAsync(ApplicationUser user, UserType userType)
    {
        string roleName = userType switch
        {
            UserType.Teacher => "Teacher",
            UserType.Student => "Student",
            UserType.Assistant => "Assistant",
            _ => throw new ArgumentOutOfRangeException(nameof(userType), userType, null)
        };

        // Assign Role
        var roleResult = await _userManager.AddToRoleAsync(user, roleName);
        if (!roleResult.Succeeded)
        {
            return Result<Guid>.Failure(roleResult.Errors.Select(e => e.Description));
        }

        // Create Business Profile
        switch (userType)
        {
            case UserType.Teacher:
                _context.TeacherProfiles.Add(new TeacherProfile { UserId = user.Id });
                await _personalOrganizations.AddForAsync(user);
                break;
            case UserType.Student:
                _context.StudentProfiles.Add(new StudentProfile { UserId = user.Id });
                break;
            case UserType.Assistant:
                _context.AssistantProfiles.Add(new AssistantProfile { UserId = user.Id });
                break;
        }

        await _context.SaveChangesAsync();
        return Result<Guid>.Success(user.Id);
    }
}
