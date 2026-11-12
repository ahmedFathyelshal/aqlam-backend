using System.Security.Cryptography;
using AqlamAcademy.Application.Common.Interfaces;
using AqlamAcademy.Application.Common.Models;
using AqlamAcademy.Application.Features.Payments;
using AqlamAcademy.Domain.Enums;
using AqlamAcademy.Domain.Learning;
using AqlamAcademy.Domain.Organizations;
using AqlamAcademy.Domain.Payments;
using AqlamAcademy.Infrastructure.Persistence.MultiTenancy;
using AqlamAcademy.Infrastructure.Persistence.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace AqlamAcademy.Infrastructure.Persistence.Payments;

/// <summary>
/// The platform's key for hashing activation codes: made once, kept encrypted in the database so every API
/// instance uses the same one and no extra secret has to be configured.
/// </summary>
public class CodeHashKey(ApplicationDbContext platform, IDataProtectionProvider protection, IMemoryCache cache)
{
    private const string SecretName = "activation-codes.hmac";
    private readonly IDataProtector _protector = protection.CreateProtector("Aqlam.PlatformSecrets.v1");

    public async Task<byte[]> GetAsync(CancellationToken cancellationToken)
    {
        if (cache.TryGetValue(SecretName, out byte[]? cached))
            return cached!;

        var stored = await platform.PlatformSecrets.AsNoTracking().FirstOrDefaultAsync(s => s.Name == SecretName, cancellationToken);
        if (stored is null)
        {
            platform.PlatformSecrets.Add(new PlatformSecret
            {
                Name = SecretName,
                ProtectedValue = _protector.Protect(Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)))
            });
            try
            {
                await platform.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException)
            {
                platform.ChangeTracker.Clear(); // another instance made it first; use theirs
            }
            stored = await platform.PlatformSecrets.AsNoTracking().FirstAsync(s => s.Name == SecretName, cancellationToken);
        }

        var key = Convert.FromBase64String(_protector.Unprotect(stored.ProtectedValue));
        cache.Set(SecretName, key);
        return key;
    }
}

/// <summary>
/// Activation codes (stories 3.1.2 and 3.1.3). Free to make; a code gives one student access to one course and makes
/// them a student of the organization (they count toward an independent teacher's monthly fee).
/// </summary>
public class ActivationCodeService(
    ApplicationDbContext platform,
    TenantDbContext tenant,
    TenantStores tenants,
    ICurrentUserService currentUser,
    ICurrentOrganization currentOrganization,
    IMembershipService memberships,
    CodeHashKey hashKey,
    Learners learners,
    IMemoryCache cache) : IActivationCodeService
{
    private const int MaxFailures = 5;
    private static readonly TimeSpan FailureWindow = TimeSpan.FromMinutes(10);

    public async Task<Result<CreatedCodeBatchDto>> CreateBatchAsync(Guid courseId, string name, int quantity, DateTime? expiresAtUtc, CancellationToken cancellationToken)
    {
        if (currentOrganization.OrganizationId is not { } orgId)
            return Result<CreatedCodeBatchDto>.NotFound("Organization not found.");
        var course = await tenant.Courses.AsNoTracking().FirstOrDefaultAsync(c => c.Id == courseId, cancellationToken);
        if (course is null)
            return Result<CreatedCodeBatchDto>.NotFound("Course not found.");
        if (course.Status is CourseStatus.Archived or CourseStatus.Suspended)
            return Result<CreatedCodeBatchDto>.Conflict("Codes cannot be made for an archived or suspended course.");

        var key = await hashKey.GetAsync(cancellationToken);
        var batch = new ActivationCodeBatch
        {
            OrganizationId = orgId,
            CourseId = courseId,
            CourseTitle = course.Title,
            Name = name,
            Quantity = quantity,
            ExpiresAtUtc = expiresAtUtc,
            CreatedByUserId = currentUser.UserId
        };

        // 31^12 possible codes: a repeat is practically impossible, but is skipped rather than stored twice.
        var plain = new List<string>(quantity);
        var hashes = new HashSet<string>();
        while (plain.Count < quantity)
        {
            var code = ActivationCodeFormat.Generate();
            var hash = ActivationCodeFormat.Hash(code, key);
            if (!hashes.Add(hash))
                continue;
            plain.Add(code);
            batch.Codes.Add(new ActivationCode { OrganizationId = orgId, CourseId = courseId, CodeHash = hash, LastFour = code[^4..] });
        }

        platform.ActivationCodeBatches.Add(batch);
        await platform.SaveChangesAsync(cancellationToken);
        return Result<CreatedCodeBatchDto>.Success(new CreatedCodeBatchDto(ToDto(batch, 0), plain.Select(ActivationCodeFormat.Display).ToList()));
    }

    public async Task<Result<IReadOnlyList<CodeBatchDto>>> ListBatchesAsync(Guid courseId, CancellationToken cancellationToken)
    {
        var orgId = currentOrganization.OrganizationId;
        var batches = await platform.ActivationCodeBatches.AsNoTracking()
            .Where(b => b.OrganizationId == orgId && b.CourseId == courseId)
            .OrderByDescending(b => b.CreatedAtUtc)
            .Select(b => new { Batch = b, Redeemed = b.Codes.Count(c => c.RedeemedByUserId != null) })
            .ToListAsync(cancellationToken);
        return Result<IReadOnlyList<CodeBatchDto>>.Success(batches.Select(b => ToDto(b.Batch, b.Redeemed)).ToList());
    }

    public async Task<Result<CodeBatchDto>> CancelBatchAsync(Guid courseId, Guid batchId, CancellationToken cancellationToken)
    {
        var orgId = currentOrganization.OrganizationId;
        var batch = await platform.ActivationCodeBatches.FirstOrDefaultAsync(b => b.Id == batchId && b.OrganizationId == orgId && b.CourseId == courseId, cancellationToken);
        if (batch is null)
            return Result<CodeBatchDto>.NotFound("Batch not found.");
        batch.CancelledAtUtc ??= DateTime.UtcNow;
        await platform.SaveChangesAsync(cancellationToken);
        var redeemed = await platform.ActivationCodes.CountAsync(c => c.BatchId == batchId && c.RedeemedByUserId != null, cancellationToken);
        return Result<CodeBatchDto>.Success(ToDto(batch, redeemed));
    }

    public async Task<Result<CodeLookupDto>> LookupAsync(Guid courseId, string code, CancellationToken cancellationToken)
    {
        var orgId = currentOrganization.OrganizationId;
        var hash = ActivationCodeFormat.Hash(ActivationCodeFormat.Clean(code), await hashKey.GetAsync(cancellationToken));
        var found = await platform.ActivationCodes.AsNoTracking().Include(c => c.Batch)
            .FirstOrDefaultAsync(c => c.CodeHash == hash && c.OrganizationId == orgId && c.CourseId == courseId, cancellationToken);
        if (found is null)
            return Result<CodeLookupDto>.NotFound("No such code for this course.");

        string? userName = null;
        if (found.RedeemedByUserId is { } userId)
            userName = await platform.Users.AsNoTracking().Where(u => u.Id == userId).Select(u => u.UserName).FirstOrDefaultAsync(cancellationToken);
        return Result<CodeLookupDto>.Success(new CodeLookupDto(StatusOf(found, DateTime.UtcNow), found.LastFour, found.BatchId, found.Batch.Name, userName, found.RedeemedAtUtc));
    }

    public async Task<Result<RedeemedDto>> RedeemAsync(string code, Guid? courseId, Guid? forUserId, CancellationToken cancellationToken)
    {
        // Wrong guesses count against the person typing; the course goes to the learner (a parent's linked child).
        var failureKey = $"code-failures:{currentUser.UserId}";
        var (userId, notLinked) = await learners.ResolveAsync(forUserId, cancellationToken);
        if (notLinked is not null)
            return Result<RedeemedDto>.Failure(notLinked.ErrorType, notLinked.Errors);
        if (cache.TryGetValue(failureKey, out Failures? failures) && failures!.Count >= MaxFailures)
            return Result<RedeemedDto>.Failure(ResultErrorType.TooManyRequests, "Too many wrong codes. Try again in 10 minutes.");

        Result<RedeemedDto> Fail(string message, ResultErrorType type = ResultErrorType.Validation)
        {
            var current = cache.GetOrCreate(failureKey, entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = FailureWindow;
                return new Failures();
            })!;
            Interlocked.Increment(ref current.Count);
            return type == ResultErrorType.Validation ? Result<RedeemedDto>.Failure([message]) : Result<RedeemedDto>.Failure(type, message);
        }

        var clean = ActivationCodeFormat.Clean(code);
        if (clean.Length != ActivationCodeFormat.Length)
            return Fail("This code is not valid. Check the letters and numbers on the card.");

        var hash = ActivationCodeFormat.Hash(clean, await hashKey.GetAsync(cancellationToken));
        var found = await platform.ActivationCodes.AsNoTracking().Include(c => c.Batch).FirstOrDefaultAsync(c => c.CodeHash == hash, cancellationToken);
        var now = DateTime.UtcNow;
        if (found is null)
            return Fail("This code is not valid. Check the letters and numbers on the card.");
        if (courseId is { } expected && found.CourseId != expected)
            return Fail("This code is not valid for this course.");
        if (found.RedeemedByUserId == userId)
            return Result<RedeemedDto>.Conflict("You already used this code.");
        switch (StatusOf(found, now))
        {
            case "Used": return Fail("This code has already been used. Please contact your center.", ResultErrorType.Conflict);
            case "Cancelled": return Fail("This code was cancelled. Please contact your center.", ResultErrorType.Conflict);
            case "Expired": return Fail("This code has expired. Please contact your center.", ResultErrorType.Conflict);
        }

        // The course must still be open; an archived course keeps only the students it had.
        await using var store = await tenants.OpenAsync(found.OrganizationId, cancellationToken);
        var course = store is null ? null : await store.Courses.AsNoTracking().FirstOrDefaultAsync(c => c.Id == found.CourseId, cancellationToken);
        if (course is null || course.Status is not (CourseStatus.Published or CourseStatus.Scheduled))
            return Result<RedeemedDto>.Conflict("This course is not open for new students. Please contact your center.");

        var enrollment = await platform.Enrollments.FirstOrDefaultAsync(e => e.UserId == userId && e.CourseId == found.CourseId, cancellationToken);
        if (enrollment?.IsUsable(now) == true)
            return Result<RedeemedDto>.Conflict("You are already enrolled in this course; the code was not used.");

        if (await StudentMembershipWriter.EnsureAsync(platform, found.OrganizationId, userId, MembershipSource.ActivationCode, cancellationToken) is { } refused)
            return Result<RedeemedDto>.Failure(refused.ErrorType, refused.Errors);

        await using var transaction = await platform.Database.BeginTransactionAsync(cancellationToken);
        // Claimed with a conditional update, so two students typing the same card at once cannot both get in.
        var claimed = await platform.ActivationCodes
            .Where(c => c.Id == found.Id && c.RedeemedByUserId == null)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.RedeemedByUserId, userId).SetProperty(c => c.RedeemedAtUtc, now), cancellationToken);
        if (claimed == 0)
            return Fail("This code has already been used. Please contact your center.", ResultErrorType.Conflict);

        EnrollmentWriter.Upsert(platform, enrollment, userId, found.OrganizationId, course, EnrollmentSource.ActivationCode);
        await platform.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        memberships.Invalidate(userId, found.OrganizationId);

        var orgSlug = await platform.Organizations.AsNoTracking().Where(o => o.Id == found.OrganizationId).Select(o => o.Slug).FirstAsync(cancellationToken);
        return Result<RedeemedDto>.Success(new RedeemedDto(orgSlug, found.CourseId, course.Title));
    }

    private static string StatusOf(ActivationCode code, DateTime now) =>
        code.RedeemedByUserId is not null ? "Used"
        : code.Batch.CancelledAtUtc is not null ? "Cancelled"
        : code.Batch.ExpiresAtUtc is { } expires && expires <= now ? "Expired"
        : "Unused";

    private static CodeBatchDto ToDto(ActivationCodeBatch b, int redeemed) =>
        new(b.Id, b.CourseId, b.CourseTitle, b.Name, b.Quantity, redeemed, b.ExpiresAtUtc, b.CreatedAtUtc, b.CancelledAtUtc);

    private sealed class Failures
    {
        public int Count;
    }
}
