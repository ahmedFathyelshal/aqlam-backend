using AqlamAcademy.Application.Common.Interfaces;
using AqlamAcademy.Application.Common.Models;
using AqlamAcademy.Application.Features.Organizations.Settings;
using AqlamAcademy.Domain.Organizations;
using Microsoft.EntityFrameworkCore;

namespace AqlamAcademy.Infrastructure.Persistence.Services;

public class OrganizationSettingsService : IOrganizationSettingsService
{
    private readonly ApplicationDbContext _platform;
    private readonly ICurrentOrganization _currentOrganization;

    public OrganizationSettingsService(ApplicationDbContext platform, ICurrentOrganization currentOrganization)
    {
        _platform = platform;
        _currentOrganization = currentOrganization;
    }

    public async Task<Result<OrganizationSettingsDto>> GetAsync(CancellationToken cancellationToken)
    {
        var organization = await _platform.Organizations.AsNoTracking().FirstOrDefaultAsync(o => o.Id == _currentOrganization.OrganizationId, cancellationToken);
        return organization is null
            ? Result<OrganizationSettingsDto>.NotFound("Organization not found.")
            : Result<OrganizationSettingsDto>.Success(ToDto(organization));
    }

    public async Task<Result<OrganizationSettingsDto>> UpdateAsync(UpdateOrganizationSettingsCommand command, CancellationToken cancellationToken)
    {
        var organization = await _platform.Organizations.FirstOrDefaultAsync(o => o.Id == _currentOrganization.OrganizationId, cancellationToken);
        if (organization is null)
            return Result<OrganizationSettingsDto>.NotFound("Organization not found.");

        organization.Name = command.Name;
        organization.TimeZone = command.TimeZone;
        organization.Calendar = command.Calendar;
        organization.WeekStartDay = command.WeekStartDay;
        organization.GradingScale = command.GradingScale;
        organization.LogoUrl = string.IsNullOrWhiteSpace(command.LogoUrl) ? null : command.LogoUrl;
        organization.PrimaryColor = string.IsNullOrWhiteSpace(command.PrimaryColor) ? null : command.PrimaryColor.ToUpperInvariant();
        organization.ContactPhone = string.IsNullOrWhiteSpace(command.ContactPhone) ? null : command.ContactPhone.Trim();
        organization.ContactEmail = string.IsNullOrWhiteSpace(command.ContactEmail) ? null : command.ContactEmail.Trim();
        organization.FollowUpAbsentDays = command.FollowUpAbsentDays;
        organization.FollowUpLateDays = command.FollowUpLateDays;
        organization.GoodGradePercent = command.GoodGradePercent;
        organization.LowGradePercent = command.LowGradePercent;
        organization.AchievementPercent = command.AchievementPercent;
        organization.AttendanceDueBy = command.AttendanceDueBy ?? organization.AttendanceDueBy;
        organization.GroupAttendanceGraceMinutes = command.GroupAttendanceGraceMinutes;
        organization.LastModifiedAtUtc = DateTime.UtcNow;
        await _platform.SaveChangesAsync(cancellationToken);

        return Result<OrganizationSettingsDto>.Success(ToDto(organization));
    }

    private static OrganizationSettingsDto ToDto(Organization o) => new(
        o.Name, o.Slug, o.Type.ToString(), o.CountryCode, o.Currency, o.TimeZone, o.Calendar.ToString(), o.WeekStartDay, o.LogoUrl, o.PrimaryColor, o.GradingScale.ToString(), o.ContactPhone, o.ContactEmail,
        o.FollowUpAbsentDays, o.FollowUpLateDays, o.GoodGradePercent, o.LowGradePercent, o.AchievementPercent,
        o.AttendanceDueBy, o.GroupAttendanceGraceMinutes);
}
