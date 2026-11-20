using AqlamAcademy.Application.Common.Interfaces;
using AqlamAcademy.Application.Common.Models;
using AqlamAcademy.Domain.Organizations;
using Microsoft.EntityFrameworkCore;

namespace AqlamAcademy.Infrastructure.Persistence.Services;

public class LegalService(ApplicationDbContext context, IAuditLog audit) : ILegalService
{
    private static readonly string[] Documents = [LegalVersions.TermsDocument, LegalVersions.PrivacyDocument];

    public async Task<LegalStatusDto> GetStatusAsync(Guid userId, CancellationToken cancellationToken)
    {
        var accepted = await context.LegalAcceptances.AsNoTracking().Where(a => a.UserId == userId).Select(a => new { a.Document, a.Version }).ToListAsync(cancellationToken);
        var documents = Documents
            .Select(d => new LegalDocumentStatusDto(d, LegalVersions.Current(d), accepted.Any(a => a.Document == d && a.Version == LegalVersions.Current(d))))
            .ToList();
        return new LegalStatusDto(documents, documents.Any(d => !d.Accepted));
    }

    public async Task AcceptCurrentAsync(Guid userId, Guid? acceptedByUserId, CancellationToken cancellationToken)
    {
        var existing = await context.LegalAcceptances.Where(a => a.UserId == userId).Select(a => new { a.Document, a.Version }).ToListAsync(cancellationToken);
        foreach (var document in Documents)
        {
            var version = LegalVersions.Current(document);
            if (existing.Any(a => a.Document == document && a.Version == version))
                continue;
            context.LegalAcceptances.Add(new LegalAcceptance { UserId = userId, Document = document, Version = version, AcceptedByUserId = acceptedByUserId });
        }
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task AcceptCurrentForNewUserAsync(string userName, CancellationToken cancellationToken)
    {
        var id = await context.Users.Where(u => u.NormalizedUserName == userName.Trim().ToUpperInvariant()).Select(u => (Guid?)u.Id).FirstOrDefaultAsync(cancellationToken);
        if (id is { } userId)
            await AcceptCurrentAsync(userId, null, cancellationToken);
    }

    public async Task<Result<DataRequestDto>> CreateRequestAsync(Guid userId, string type, CancellationToken cancellationToken)
    {
        if (!Enum.TryParse<DataRequestType>(type, ignoreCase: true, out var parsed) || !Enum.IsDefined(parsed))
            return Result<DataRequestDto>.Failure(["Request type must be Export or Delete."]);

        var open = await context.DataRequests.AnyAsync(r => r.UserId == userId && r.Type == parsed && r.Status == DataRequestStatus.Open, cancellationToken);
        if (open)
            return Result<DataRequestDto>.Conflict("You already have an open request of this kind.");

        var request = new DataRequest { UserId = userId, Type = parsed };
        context.DataRequests.Add(request);
        await context.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync($"data-request.{parsed.ToString().ToLowerInvariant()}.created", userId, userId, null, "Personal data request submitted.", cancellationToken);
        return Result<DataRequestDto>.Success(ToDto(request, null));
    }

    public async Task<IReadOnlyList<DataRequestDto>> ListMineAsync(Guid userId, CancellationToken cancellationToken) =>
        (await context.DataRequests.AsNoTracking().Where(r => r.UserId == userId).OrderByDescending(r => r.CreatedAtUtc).ToListAsync(cancellationToken))
            .Select(r => ToDto(r, null)).ToList();

    public async Task<IReadOnlyList<DataRequestDto>> ListAllAsync(string? status, CancellationToken cancellationToken)
    {
        var query = context.DataRequests.AsNoTracking().AsQueryable();
        if (Enum.TryParse<DataRequestStatus>(status, ignoreCase: true, out var parsed))
            query = query.Where(r => r.Status == parsed);

        var rows = await (from r in query
                          join u in context.Users.IgnoreQueryFilters().AsNoTracking() on r.UserId equals u.Id
                          orderby r.CreatedAtUtc descending
                          select new { Request = r, u.UserName }).Take(200).ToListAsync(cancellationToken);
        return rows.Select(x => ToDto(x.Request, x.UserName)).ToList();
    }

    public async Task<Result> HandleRequestAsync(Guid actorUserId, Guid requestId, bool completed, string? note, CancellationToken cancellationToken)
    {
        var request = await context.DataRequests.FirstOrDefaultAsync(r => r.Id == requestId, cancellationToken);
        if (request is null)
            return Result.Failure(["Request not found."]);
        if (request.Status != DataRequestStatus.Open)
            return Result.Failure(["This request was already handled."]);

        request.Status = completed ? DataRequestStatus.Completed : DataRequestStatus.Rejected;
        request.Note = note is { Length: > 500 } ? note[..500] : note;
        request.HandledAtUtc = DateTime.UtcNow;
        request.HandledByUserId = actorUserId;
        await context.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync($"data-request.{request.Type.ToString().ToLowerInvariant()}.{(completed ? "completed" : "rejected")}", actorUserId, request.UserId, null, request.Note, cancellationToken);
        return Result.Success();
    }

    private static DataRequestDto ToDto(DataRequest r, string? userName) =>
        new(r.Id, userName, r.Type.ToString(), r.Status.ToString(), r.Note, r.CreatedAtUtc, r.HandledAtUtc, r.UserId);
}
