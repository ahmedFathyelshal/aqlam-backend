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

    public IdentityService(
        UserManager<ApplicationUser> userManager,
        ApplicationDbContext context,
        ILogger<IdentityService> logger)
    {
        _userManager = userManager;
        _context = context;
        _logger = logger;
    }

    public async Task<bool> IsUserNameUniqueAsync(string userName)
    {
        return await _userManager.FindByNameAsync(userName) == null;
    }

    public async Task<Result<Guid>> CreateUserAsync(
        string userName, 
        string email,
        string password, 
        string fullName, 
        UserType userType)
    {
        using var transaction = await _context.Database.BeginTransactionAsync();

        try
        {
            var user = new ApplicationUser
            {
                UserName = userName,
                Email = email,
                FullName = fullName,
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
