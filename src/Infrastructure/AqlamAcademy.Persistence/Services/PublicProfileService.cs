using AqlamAcademy.Application.Common.Interfaces;
using AqlamAcademy.Application.Common.Models;
using AqlamAcademy.Application.Features.Marketplace;
using AqlamAcademy.Domain.Marketplace;
using AqlamAcademy.Domain.Organizations;
using AqlamAcademy.Domain.Staff;
using Microsoft.EntityFrameworkCore;

namespace AqlamAcademy.Infrastructure.Persistence.Services;

/// <summary>
/// The independent teacher's public page and search listing. Only the owner of a personal academy has one:
/// schools and centers are never listed as teachers.
/// </summary>
public class PublicProfileService(
    ApplicationDbContext platform,
    ICurrentUserService currentUser,
    ICurrentOrganization currentOrganization,
    MarketplaceIndex index) : IPublicProfileService
{
    public async Task<Result<PublicProfileDto>> GetAsync(CancellationToken cancellationToken)
    {
        var (org, failure) = await OwnPersonalAcademyAsync(cancellationToken);
        return failure ?? Result<PublicProfileDto>.Success(await ToDtoAsync(org!, cancellationToken));
    }

    public async Task<Result<PublicProfileDto>> UpdateAsync(UpdatePublicProfileCommand command, CancellationToken cancellationToken)
    {
        var (org, failure) = await OwnPersonalAcademyAsync(cancellationToken);
        if (failure is not null)
            return failure;

        var profile = await ProfileAsync(cancellationToken);
        profile.PublicName = Clean(command.PublicName);
        profile.Headline = Clean(command.Headline);
        profile.Bio = Clean(command.Bio);
        profile.City = Clean(command.City);
        profile.IsListed = command.IsListed;
        profile.LastModifiedAtUtc = DateTime.UtcNow;

        var user = await platform.Users.FirstAsync(u => u.Id == currentUser.UserId, cancellationToken);
        user.Gender = command.Gender ?? user.Gender;
        await platform.SaveChangesAsync(cancellationToken);

        await index.RefreshTeacherAsync(org!.Id, cancellationToken);
        return Result<PublicProfileDto>.Success(await ToDtoAsync(org, cancellationToken));
    }

    public async Task<Result<PublicProfileDto>> SavePhotoAsync(byte[] content, CancellationToken cancellationToken)
    {
        var (org, failure) = await OwnPersonalAcademyAsync(cancellationToken);
        if (failure is not null)
            return failure;

        var profile = await ProfileAsync(cancellationToken);
        var photo = await platform.TeacherPhotos.FirstOrDefaultAsync(p => p.UserId == currentUser.UserId, cancellationToken);
        if (photo is null)
        {
            photo = new TeacherPhoto { UserId = currentUser.UserId };
            platform.TeacherPhotos.Add(photo);
        }
        photo.Content = content;
        photo.ContentType = OrganizationLogo.DetectContentType(content)!;
        photo.UpdatedAtUtc = DateTime.UtcNow;
        profile.PhotoUpdatedAtUtc = photo.UpdatedAtUtc;
        await platform.SaveChangesAsync(cancellationToken);

        await index.RefreshTeacherAsync(org!.Id, cancellationToken);
        return Result<PublicProfileDto>.Success(await ToDtoAsync(org, cancellationToken));
    }

    public async Task<Result<PublicProfileDto>> RemovePhotoAsync(CancellationToken cancellationToken)
    {
        var (org, failure) = await OwnPersonalAcademyAsync(cancellationToken);
        if (failure is not null)
            return failure;

        await platform.TeacherPhotos.Where(p => p.UserId == currentUser.UserId).ExecuteDeleteAsync(cancellationToken);
        var profile = await ProfileAsync(cancellationToken);
        profile.PhotoUpdatedAtUtc = null;
        await platform.SaveChangesAsync(cancellationToken);

        await index.RefreshTeacherAsync(org!.Id, cancellationToken);
        return Result<PublicProfileDto>.Success(await ToDtoAsync(org, cancellationToken));
    }

    private async Task<(Organization? Org, Result<PublicProfileDto>? Failure)> OwnPersonalAcademyAsync(CancellationToken cancellationToken)
    {
        var org = await platform.Organizations.AsNoTracking().FirstOrDefaultAsync(o => o.Id == currentOrganization.OrganizationId, cancellationToken);
        if (org is null)
            return (null, Result<PublicProfileDto>.NotFound("Organization not found."));
        var isOwner = await platform.Memberships.AnyAsync(m => m.OrganizationId == org.Id && m.UserId == currentUser.UserId
            && m.Status == MembershipStatus.Active && (m.Roles & OrganizationRole.Owner) == OrganizationRole.Owner, cancellationToken);
        if (org.Type != OrganizationType.Personal || !isOwner)
            return (null, Result<PublicProfileDto>.Failure(Application.Common.Models.ResultErrorType.Forbidden,
                "Only an independent teacher has a public teacher page."));
        return (org, null);
    }

    private async Task<TeacherProfile> ProfileAsync(CancellationToken cancellationToken)
    {
        var profile = await platform.TeacherProfiles.FirstOrDefaultAsync(p => p.UserId == currentUser.UserId, cancellationToken);
        if (profile is null)
        {
            profile = new TeacherProfile { UserId = currentUser.UserId };
            platform.TeacherProfiles.Add(profile);
        }
        return profile;
    }

    private async Task<PublicProfileDto> ToDtoAsync(Organization org, CancellationToken cancellationToken)
    {
        var me = currentUser.UserId;
        var profile = await platform.TeacherProfiles.AsNoTracking().FirstOrDefaultAsync(p => p.UserId == me, cancellationToken);
        var gender = await platform.Users.AsNoTracking().Where(u => u.Id == me).Select(u => u.Gender).FirstAsync(cancellationToken);
        var courses = await platform.MarketplaceCourses.AsNoTracking().CountAsync(c => c.OrganizationId == org.Id, cancellationToken);

        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(profile?.PublicName)) missing.Add("publicName");
        if (gender is null) missing.Add("gender");
        if (courses == 0) missing.Add("publicCourse"); // listed anyway, but a page without courses helps nobody
        return new PublicProfileDto(org.Slug, profile?.PublicName, profile?.Headline, profile?.Bio, profile?.City, gender?.ToString(),
            profile?.IsListed == true, profile?.PhotoUpdatedAtUtc is { } at ? TeacherPhoto.PublicPath(org.Slug, at) : null, courses, missing);
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <summary>Platform admin: featured teachers (story 3.5.4), fee rates (3.6.1) and per-organization commission (3.2.2).</summary>
public class PlatformMarketplaceService(ApplicationDbContext platform) : IPlatformMarketplaceService
{
    public async Task<IReadOnlyList<FeaturedTeacherDto>> ListFeaturedAsync(CancellationToken cancellationToken)
    {
        var rows = await (from f in platform.FeaturedTeachers.AsNoTracking()
                          join t in platform.MarketplaceTeachers.AsNoTracking() on f.OrganizationId equals t.OrganizationId
                          orderby f.SubjectId, f.Order, t.Name
                          select new { f, t }).ToListAsync(cancellationToken);
        return rows.Select(x => new FeaturedTeacherDto(x.f.Id, x.f.SubjectId, x.f.Order, Card(x.t))).ToList();
    }

    public async Task<IReadOnlyList<AdminTeacherDto>> ListedTeachersAsync(string? q, CancellationToken cancellationToken)
    {
        var query = platform.MarketplaceTeachers.AsNoTracking();
        foreach (var term in SearchText.Terms(q))
            query = query.Where(t => t.SearchText.Contains(term) || t.Slug.Contains(term));
        return await query.OrderBy(t => t.Name).Take(50)
            .Select(t => new AdminTeacherDto(t.OrganizationId, t.Slug, t.Name, t.CountryCode, t.CourseCount))
            .ToListAsync(cancellationToken);
    }

    public async Task<Result<FeaturedTeacherDto>> FeatureAsync(FeatureTeacherCommand command, CancellationToken cancellationToken)
    {
        var teacher = await platform.MarketplaceTeachers.AsNoTracking().FirstOrDefaultAsync(t => t.Slug == command.Slug.ToLower(), cancellationToken);
        if (teacher is null)
            return Result<FeaturedTeacherDto>.NotFound("Only teachers shown in search can be featured.");
        if (command.SubjectId is { } subjectId && !await platform.Subjects.AnyAsync(s => s.Id == subjectId, cancellationToken))
            return Result<FeaturedTeacherDto>.Failure(["Unknown subject."]);

        var pick = await platform.FeaturedTeachers.FirstOrDefaultAsync(f => f.OrganizationId == teacher.OrganizationId && f.SubjectId == command.SubjectId, cancellationToken);
        if (pick is null)
        {
            pick = new Domain.Marketplace.FeaturedTeacher { OrganizationId = teacher.OrganizationId, SubjectId = command.SubjectId };
            platform.FeaturedTeachers.Add(pick);
        }
        pick.Order = command.Order;
        await platform.SaveChangesAsync(cancellationToken);
        return Result<FeaturedTeacherDto>.Success(new FeaturedTeacherDto(pick.Id, pick.SubjectId, pick.Order, Card(teacher)));
    }

    public async Task<Result> UnfeatureAsync(Guid id, CancellationToken cancellationToken)
    {
        var removed = await platform.FeaturedTeachers.Where(f => f.Id == id).ExecuteDeleteAsync(cancellationToken);
        return removed == 0 ? Result<object>.NotFound("Pick not found.") : Result.Success();
    }

    public async Task<FeesDto> GetFeesAsync(CancellationToken cancellationToken)
    {
        var fees = await platform.PlatformFees.AsNoTracking().FirstOrDefaultAsync(cancellationToken);
        return new FeesDto(fees?.CommissionPercent, fees?.MonthlyStudentFeeSar, fees?.MonthlyStudentFeeEgp);
    }

    public async Task<FeesDto> SaveFeesAsync(SaveFeesCommand command, CancellationToken cancellationToken)
    {
        var fees = await platform.PlatformFees.FirstOrDefaultAsync(cancellationToken);
        if (fees is null)
        {
            fees = new Domain.Payments.PlatformFees();
            platform.PlatformFees.Add(fees);
        }
        fees.CommissionPercent = command.CommissionPercent;
        fees.MonthlyStudentFeeSar = command.MonthlyStudentFeeSar;
        fees.MonthlyStudentFeeEgp = command.MonthlyStudentFeeEgp;
        fees.UpdatedAtUtc = DateTime.UtcNow;
        await platform.SaveChangesAsync(cancellationToken);
        return new FeesDto(fees.CommissionPercent, fees.MonthlyStudentFeeSar, fees.MonthlyStudentFeeEgp);
    }

    public async Task<Result> SetCommissionAsync(string slug, decimal? commissionPercent, CancellationToken cancellationToken)
    {
        var updated = await platform.Organizations.Where(o => o.Slug == slug.ToLower())
            .ExecuteUpdateAsync(s => s.SetProperty(o => o.CommissionPercent, commissionPercent), cancellationToken);
        return updated == 0 ? Result<object>.NotFound("Organization not found.") : Result.Success();
    }

    private static TeacherCardDto Card(MarketplaceTeacher t) =>
        new(t.Slug, t.Name, t.Headline, t.City, t.CountryCode, t.Gender.ToString(), t.PhotoUrl, t.SubjectIds, t.GradeIds, t.CourseCount);
}
