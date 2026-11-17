using AqlamAcademy.Application.Common.Models;

namespace AqlamAcademy.Application.Common.Interfaces;

public record LegalDocumentStatusDto(string Document, string Version, bool Accepted);
public record LegalStatusDto(IReadOnlyList<LegalDocumentStatusDto> Documents, bool Pending);
public record DataRequestDto(Guid Id, string? UserName, string Type, string Status, string? Note, DateTime CreatedAtUtc, DateTime? HandledAtUtc);

/// <summary>Versioned terms and privacy acceptance, and export/delete requests for personal data.</summary>
public interface ILegalService
{
    Task<LegalStatusDto> GetStatusAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>Records acceptance of the current versions. <paramref name="acceptedByUserId"/> is set when a parent accepts for a child.</summary>
    Task AcceptCurrentAsync(Guid userId, Guid? acceptedByUserId, CancellationToken cancellationToken);

    /// <summary>Same, for an account that was just created and is identified by its username.</summary>
    Task AcceptCurrentForNewUserAsync(string userName, CancellationToken cancellationToken);

    Task<Result<DataRequestDto>> CreateRequestAsync(Guid userId, string type, CancellationToken cancellationToken);
    Task<IReadOnlyList<DataRequestDto>> ListMineAsync(Guid userId, CancellationToken cancellationToken);
    Task<IReadOnlyList<DataRequestDto>> ListAllAsync(string? status, CancellationToken cancellationToken);
    Task<Result> HandleRequestAsync(Guid actorUserId, Guid requestId, bool completed, string? note, CancellationToken cancellationToken);
}
