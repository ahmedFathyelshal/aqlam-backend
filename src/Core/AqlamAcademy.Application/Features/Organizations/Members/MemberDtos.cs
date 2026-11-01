namespace AqlamAcademy.Application.Features.Organizations.Members;

public record MemberDto(
    Guid UserId,
    string UserName,
    string FullName,
    IReadOnlyList<string> Roles,
    string Status,
    string Source,
    DateTime JoinedAtUtc);

public record JoinCodeDto(
    Guid Id,
    string Code,
    string Role,
    bool RequiresApproval,
    DateTime? ExpiresAtUtc,
    int? MaxUses,
    int UsesCount,
    bool IsActive);

public record JoinResultDto(
    Guid OrganizationId,
    string Slug,
    string Name,
    string Type,
    string MembershipStatus);
