using AqlamAcademy.Application.Common.Models;
using AqlamAcademy.Domain.Organizations;
using FluentValidation;
using MediatR;

namespace AqlamAcademy.Application.Features.Organizations.Settings;

public record OrganizationSettingsDto(
    string Name,
    string Slug,
    string Type,
    string CountryCode,
    string Currency,
    string TimeZone,
    string Calendar,
    DayOfWeek WeekStartDay,
    string? LogoUrl,
    string? PrimaryColor,
    string GradingScale = "Descriptive",
    string? ContactPhone = null,
    string? ContactEmail = null,
    int FollowUpAbsentDays = 3,
    int FollowUpLateDays = 5,
    int GoodGradePercent = 75,
    int LowGradePercent = 60,
    int AchievementPercent = 85,
    TimeOnly? AttendanceDueBy = null,
    int GroupAttendanceGraceMinutes = 30);

public interface IOrganizationSettingsService
{
    Task<Result<OrganizationSettingsDto>> GetAsync(CancellationToken cancellationToken);
    Task<Result<OrganizationSettingsDto>> UpdateAsync(UpdateOrganizationSettingsCommand command, CancellationToken cancellationToken);
}

public record GetOrganizationSettingsQuery : IRequest<Result<OrganizationSettingsDto>>;

public class GetOrganizationSettingsQueryHandler(IOrganizationSettingsService settings) : IRequestHandler<GetOrganizationSettingsQuery, Result<OrganizationSettingsDto>>
{
    public Task<Result<OrganizationSettingsDto>> Handle(GetOrganizationSettingsQuery request, CancellationToken cancellationToken) =>
        settings.GetAsync(cancellationToken);
}

/// <summary>Profile and locale of the organization. Slug, country and currency change only through the platform team.</summary>
public record UpdateOrganizationSettingsCommand(
    string Name,
    string TimeZone,
    CalendarDisplay Calendar,
    DayOfWeek WeekStartDay,
    string? LogoUrl,
    string? PrimaryColor,
    AqlamAcademy.Domain.Academics.GradingScale GradingScale = AqlamAcademy.Domain.Academics.GradingScale.Descriptive,
    string? ContactPhone = null,
    string? ContactEmail = null,
    int FollowUpAbsentDays = 3,
    int FollowUpLateDays = 5,
    int GoodGradePercent = 75,
    int LowGradePercent = 60,
    int AchievementPercent = 85,
    TimeOnly? AttendanceDueBy = null,
    int GroupAttendanceGraceMinutes = 30) : IRequest<Result<OrganizationSettingsDto>>;

public class UpdateOrganizationSettingsCommandValidator : AbstractValidator<UpdateOrganizationSettingsCommand>
{
    public UpdateOrganizationSettingsCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(150);
        RuleFor(x => x.TimeZone).Must(tz => TimeZoneInfo.TryFindSystemTimeZoneById(tz ?? string.Empty, out _))
            .WithMessage("Unknown time zone. Use an IANA name such as Asia/Riyadh or Africa/Cairo.");
        RuleFor(x => x.Calendar).IsInEnum();
        RuleFor(x => x.WeekStartDay).IsInEnum();
        RuleFor(x => x.GradingScale).IsInEnum();
        RuleFor(x => x.LogoUrl)
            .Must(url => Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps)
            .When(x => !string.IsNullOrEmpty(x.LogoUrl)).WithMessage("The logo must be an https URL.");
        RuleFor(x => x.PrimaryColor).Matches("^#[0-9a-fA-F]{6}$")
            .When(x => !string.IsNullOrEmpty(x.PrimaryColor)).WithMessage("Use a color like #1E6FD9.");
        RuleFor(x => x.ContactPhone).Matches(@"^\+?[0-9][0-9 ()-]{5,30}$")
            .When(x => !string.IsNullOrWhiteSpace(x.ContactPhone)).WithMessage("Enter a valid phone number.");
        RuleFor(x => x.ContactEmail).EmailAddress().MaximumLength(256)
            .When(x => !string.IsNullOrWhiteSpace(x.ContactEmail)).WithMessage("Enter a valid email address.");
        // Attendance is judged over the latest 20 school days.
        RuleFor(x => x.FollowUpAbsentDays).InclusiveBetween(1, 20);
        RuleFor(x => x.FollowUpLateDays).InclusiveBetween(1, 20);
        RuleFor(x => x.GoodGradePercent).InclusiveBetween(1, 100);
        RuleFor(x => x.LowGradePercent).InclusiveBetween(0, 100)
            .LessThan(x => x.GoodGradePercent).WithMessage("The low grade limit must be below the good grade limit.");
        RuleFor(x => x.AchievementPercent).InclusiveBetween(1, 100);
        RuleFor(x => x.AttendanceDueBy).InclusiveBetween(new TimeOnly(5, 0), new TimeOnly(23, 0))
            .When(x => x.AttendanceDueBy is not null).WithMessage("Choose a time between 05:00 and 23:00.");
        RuleFor(x => x.GroupAttendanceGraceMinutes).InclusiveBetween(0, 240);
    }
}

public class UpdateOrganizationSettingsCommandHandler(IOrganizationSettingsService settings) : IRequestHandler<UpdateOrganizationSettingsCommand, Result<OrganizationSettingsDto>>
{
    public Task<Result<OrganizationSettingsDto>> Handle(UpdateOrganizationSettingsCommand request, CancellationToken cancellationToken) =>
        settings.UpdateAsync(request with { Name = request.Name.Trim() }, cancellationToken);
}
