namespace AqlamAcademy.Application.Features.Platform.Organizations;

public record OrganizationDto(
    Guid Id,
    string Name,
    string Slug,
    string Type,
    string Status,
    string CountryCode,
    string Currency,
    string TimeZone,
    string IsolationTier,
    string PublicPublishing,
    string? PrimaryHost);
