using AqlamAcademy.Application.Common.Interfaces;
using AqlamAcademy.Application.Common.Models;
using MediatR;

namespace AqlamAcademy.Application.Features.Organizations.Members;

public record MemberImportRowDto(
    int RowNumber,
    string FullName,
    string Role,
    string Status,
    string? UserName,
    string? TemporaryPassword,
    IReadOnlyList<string> Errors);

/// <summary>
/// Result of an import. Temporary passwords are returned only here (never stored in clear text),
/// so the caller must print or hand them out now.
/// </summary>
public record MemberImportReportDto(int Created, int Failed, IReadOnlyList<MemberImportRowDto> Rows);

public interface IMemberImportService
{
    Task<Result<MemberImportReportDto>> ImportAsync(Guid organizationId, Stream workbook, CancellationToken cancellationToken);
    byte[] BuildTemplate();
}

public record ImportMembersCommand(Stream Workbook) : IRequest<Result<MemberImportReportDto>>;

public class ImportMembersCommandHandler(IMemberImportService importer, ICurrentOrganization organization)
    : IRequestHandler<ImportMembersCommand, Result<MemberImportReportDto>>
{
    public Task<Result<MemberImportReportDto>> Handle(ImportMembersCommand request, CancellationToken cancellationToken) =>
        organization.OrganizationId is { } orgId
            ? importer.ImportAsync(orgId, request.Workbook, cancellationToken)
            : Task.FromResult(Result<MemberImportReportDto>.NotFound("Organization not found."));
}
