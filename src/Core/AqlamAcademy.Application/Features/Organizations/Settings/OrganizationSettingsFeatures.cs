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
    string? PrimaryColor);

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
    string? PrimaryColor) : IRequest<Result<OrganizationSettingsDto>>;

public class UpdateOrganizationSettingsCommandValidator : AbstractValidator<UpdateOrganizationSettingsCommand>
{
    public UpdateOrganizationSettingsCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(150);
        RuleFor(x => x.TimeZone).Must(tz => TimeZoneInfo.TryFindSystemTimeZoneById(tz ?? string.Empty, out _))
            .WithMessage("Unknown time zone. Use an IANA name such as Asia/Riyadh or Africa/Cairo.");
        RuleFor(x => x.Calendar).IsInEnum();
        RuleFor(x => x.WeekStartDay).IsInEnum();
        RuleFor(x => x.LogoUrl)
            .Must(url => Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps)
            .When(x => !string.IsNullOrEmpty(x.LogoUrl)).WithMessage("The logo must be an https URL.");
        RuleFor(x => x.PrimaryColor).Matches("^#[0-9a-fA-F]{6}$")
            .When(x => !string.IsNullOrEmpty(x.PrimaryColor)).WithMessage("Use a color like #1E6FD9.");
    }
}

public class UpdateOrganizationSettingsCommandHandler(IOrganizationSettingsService settings) : IRequestHandler<UpdateOrganizationSettingsCommand, Result<OrganizationSettingsDto>>
{
    public Task<Result<OrganizationSettingsDto>> Handle(UpdateOrganizationSettingsCommand request, CancellationToken cancellationToken) =>
        settings.UpdateAsync(request with { Name = request.Name.Trim() }, cancellationToken);
}
