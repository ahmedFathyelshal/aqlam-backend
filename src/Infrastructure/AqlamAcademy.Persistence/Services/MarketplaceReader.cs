using AqlamAcademy.Application.Common.Interfaces;
using AqlamAcademy.Application.Common.Models;
using AqlamAcademy.Application.Features.Marketplace;
using AqlamAcademy.Domain.Enums;
using AqlamAcademy.Domain.Marketplace;
using AqlamAcademy.Domain.Organizations;
using AqlamAcademy.Domain.Payments;
using AqlamAcademy.Infrastructure.Persistence.MultiTenancy;
using AqlamAcademy.Infrastructure.Persistence.Payments;
using Microsoft.EntityFrameworkCore;

namespace AqlamAcademy.Infrastructure.Persistence.Services;

/// <summary>
/// Public marketplace pages. Reads only the marketplace copy, except the course page and free previews, which open
/// the course's own store for the outline. Results are never ranked: courses come newest first, teachers by name.
/// </summary>
public class MarketplaceReader(
    ApplicationDbContext platform,
    TenantStores tenants,
    ICurrentUserService currentUser,
    PaymentProviders providers,
    Learners learners) : IMarketplaceReader
{
    private const int MaxPageSize = 48;

    public async Task<PagedDto<CourseCardDto>> SearchCoursesAsync(MarketplaceSearch search, CancellationToken cancellationToken)
    {
        var query = platform.MarketplaceCourses.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(search.Country))
            query = query.Where(c => c.CountryCode == search.Country.ToUpperInvariant());
        if (search.CurriculumId is { } curriculumId)
            query = query.Where(c => c.CurriculumId == curriculumId);
        if (search.GradeId is { } gradeId)
            query = query.Where(c => c.GradeId == gradeId);
        if (search.SubjectId is { } subjectId)
            query = query.Where(c => c.SubjectId == subjectId);
        if (search.TeacherGender is { } gender)
            query = query.Where(c => c.TeacherGender == gender);
        if (search.Free is { } free)
            query = query.Where(c => c.IsFree == free);
        foreach (var term in SearchText.Terms(search.Q))
            query = query.Where(c => c.SearchText.Contains(term) || c.ProviderSearchText.Contains(term));

        var (page, size) = Paging(search);
        var total = await query.CountAsync(cancellationToken);
        var rows = await query
            .OrderBy(c => c.IsComingSoon).ThenByDescending(c => c.PublishedAtUtc).ThenBy(c => c.CourseId)
            .Skip((page - 1) * size).Take(size)
            .ToListAsync(cancellationToken);
        return new PagedDto<CourseCardDto>(await CardsAsync(rows, cancellationToken), total, page, size);
    }

    public async Task<PagedDto<TeacherCardDto>> SearchTeachersAsync(MarketplaceSearch search, CancellationToken cancellationToken)
    {
        var query = platform.MarketplaceTeachers.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(search.Country))
            query = query.Where(t => t.CountryCode == search.Country.ToUpperInvariant());
        if (search.CurriculumId is { } curriculumId)
            query = query.Where(t => t.CurriculumIds.Contains(curriculumId));
        if (search.GradeId is { } gradeId)
            query = query.Where(t => t.GradeIds.Contains(gradeId));
        if (search.SubjectId is { } subjectId)
            query = query.Where(t => t.SubjectIds.Contains(subjectId));
        if (search.TeacherGender is { } gender)
            query = query.Where(t => t.Gender == gender);
        foreach (var term in SearchText.Terms(search.Q))
            query = query.Where(t => t.SearchText.Contains(term));

        var (page, size) = Paging(search);
        var total = await query.CountAsync(cancellationToken);
        var rows = await query.OrderBy(t => t.Name).ThenBy(t => t.OrganizationId)
            .Skip((page - 1) * size).Take(size).ToListAsync(cancellationToken);
        return new PagedDto<TeacherCardDto>(rows.Select(ToCard).ToList(), total, page, size);
    }

    public async Task<FacetsDto> FacetsAsync(string? country, CancellationToken cancellationToken)
    {
        var courses = platform.MarketplaceCourses.AsNoTracking();
        var teachers = platform.MarketplaceTeachers.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(country))
        {
            var code = country.ToUpperInvariant();
            courses = courses.Where(c => c.CountryCode == code);
            teachers = teachers.Where(t => t.CountryCode == code);
        }
        var rows = await courses.Select(c => new { c.SubjectId, c.GradeId, c.CurriculumId }).ToListAsync(cancellationToken);
        static List<CountDto> Count(IEnumerable<int?> ids) => ids.Where(i => i != null).GroupBy(i => i!.Value)
            .Select(g => new CountDto(g.Key, g.Count())).OrderBy(c => c.Id).ToList();
        return new FacetsDto(Count(rows.Select(r => r.SubjectId)), Count(rows.Select(r => r.GradeId)), Count(rows.Select(r => r.CurriculumId)),
            rows.Count, await teachers.CountAsync(cancellationToken));
    }

    public async Task<IReadOnlyList<FeaturedTeacherDto>> FeaturedAsync(int? subjectId, string? country, CancellationToken cancellationToken)
    {
        var query = from f in platform.FeaturedTeachers.AsNoTracking()
                    join t in platform.MarketplaceTeachers.AsNoTracking() on f.OrganizationId equals t.OrganizationId
                    select new { f, t };
        if (subjectId is { } id)
            query = query.Where(x => x.f.SubjectId == id);
        if (!string.IsNullOrWhiteSpace(country))
            query = query.Where(x => x.t.CountryCode == country.ToUpperInvariant());
        var rows = await query.OrderBy(x => x.f.SubjectId).ThenBy(x => x.f.Order).ThenBy(x => x.t.Name).ToListAsync(cancellationToken);
        return rows.Select(x => new FeaturedTeacherDto(x.f.Id, x.f.SubjectId, x.f.Order, ToCard(x.t))).ToList();
    }

    public async Task<Result<TeacherPageDto>> TeacherAsync(string slug, CancellationToken cancellationToken)
    {
        var teacher = await platform.MarketplaceTeachers.AsNoTracking().FirstOrDefaultAsync(t => t.Slug == slug.ToLower(), cancellationToken);
        if (teacher is null)
            return Result<TeacherPageDto>.NotFound("Teacher not found.");
        var bio = await platform.TeacherProfiles.AsNoTracking().Where(p => p.UserId == teacher.TeacherUserId).Select(p => p.Bio).FirstOrDefaultAsync(cancellationToken);
        var courses = await platform.MarketplaceCourses.AsNoTracking().Where(c => c.OrganizationId == teacher.OrganizationId)
            .OrderBy(c => c.IsComingSoon).ThenByDescending(c => c.PublishedAtUtc).ToListAsync(cancellationToken);
        return Result<TeacherPageDto>.Success(new TeacherPageDto(ToCard(teacher), bio, await CardsAsync(courses, cancellationToken)));
    }

    public async Task<Result<CoursePageDto>> CourseAsync(string slug, CancellationToken cancellationToken)
    {
        var row = await FindCourseAsync(slug, cancellationToken);
        if (row is null)
            return Result<CoursePageDto>.NotFound("Course not found.");

        await using var tenant = await tenants.OpenAsync(row.OrganizationId, cancellationToken);
        var course = tenant is null ? null : await tenant.Courses.AsNoTracking()
            .Include(c => c.Chapters).ThenInclude(ch => ch.Lessons)
            .AsSplitQuery()
            .FirstOrDefaultAsync(c => c.Id == row.CourseId, cancellationToken);
        if (course is null || !MarketplaceIndex.CanList(course))
            return Result<CoursePageDto>.NotFound("Course not found.");

        var chapters = course.Chapters.Where(c => !c.IsDeleted).OrderBy(c => c.Order)
            .Select(c => new OutlineChapterDto(c.Title, c.Lessons.Where(l => !l.IsDeleted && l.IsPublished).OrderBy(l => l.Order)
                .Select(l => new OutlineLessonDto(l.Id, l.Title, l.Type.ToString(), l.DurationInMinutes, l.IsFreePreview)).ToList()))
            .Where(c => c.Lessons.Count > 0)
            .ToList();

        var teacher = await platform.MarketplaceTeachers.AsNoTracking().FirstOrDefaultAsync(t => t.OrganizationId == row.OrganizationId, cancellationToken);
        var card = (await CardsAsync([row], cancellationToken))[0];
        return Result<CoursePageDto>.Success(new CoursePageDto(
            card, row.CourseId, row.OrganizationSlug, course.PromoVideoUrl, course.SyllabusRequirements,
            (course.AccessExpiryPolicy ?? AccessExpiryPolicy.Unlimited).ToString(), course.AccessDurationDays,
            chapters, teacher is null ? null : ToCard(teacher), providers.OnlinePaymentAvailable,
            await ViewerAsync(row, cancellationToken)));
    }

    public async Task<Result<PreviewLessonDto>> PreviewAsync(string slug, Guid lessonId, CancellationToken cancellationToken)
    {
        var row = await FindCourseAsync(slug, cancellationToken);
        if (row is null)
            return Result<PreviewLessonDto>.NotFound("Course not found.");
        await using var tenant = await tenants.OpenAsync(row.OrganizationId, cancellationToken);
        if (tenant is null)
            return Result<PreviewLessonDto>.NotFound("Course not found.");

        var lesson = await tenant.CourseLessons.AsNoTracking()
            .Where(l => l.Id == lessonId && l.Chapter.CourseId == row.CourseId && l.IsPublished && l.IsFreePreview && !l.Chapter.IsDeleted)
            .FirstOrDefaultAsync(cancellationToken);
        return lesson is null
            ? Result<PreviewLessonDto>.NotFound("This lesson is not a free preview.")
            : Result<PreviewLessonDto>.Success(new PreviewLessonDto(lesson.Id, lesson.Title, lesson.Type.ToString(),
                lesson.VideoUrl, lesson.Summary, lesson.Content, lesson.DurationInMinutes));
    }

    public async Task<(byte[] Content, string ContentType)?> TeacherPhotoAsync(string slug, CancellationToken cancellationToken)
    {
        var photo = await (from t in platform.MarketplaceTeachers.AsNoTracking()
                           join p in platform.TeacherPhotos.AsNoTracking() on t.TeacherUserId equals p.UserId
                           where t.Slug == slug.ToLower()
                           select new { p.Content, p.ContentType }).FirstOrDefaultAsync(cancellationToken);
        return photo is null ? null : (photo.Content, photo.ContentType);
    }

    /// <summary>By the full slug, or by its short code when the title (and so the slug) changed since the link was shared.</summary>
    private async Task<MarketplaceCourse?> FindCourseAsync(string slug, CancellationToken cancellationToken)
    {
        var bySlug = await platform.MarketplaceCourses.AsNoTracking().FirstOrDefaultAsync(c => c.PublicSlug == slug, cancellationToken);
        if (bySlug is not null)
            return bySlug;
        var code = slug[(slug.LastIndexOf('-') + 1)..].ToLowerInvariant();
        return code.Length == 8
            ? await platform.MarketplaceCourses.AsNoTracking().FirstOrDefaultAsync(c => c.ShortCode == code, cancellationToken)
            : null;
    }

    private async Task<CourseViewerDto> ViewerAsync(MarketplaceCourse row, CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated)
            return new CourseViewerDto(false, false, false, false);
        var me = currentUser.UserId;
        var now = DateTime.UtcNow;
        var enrollment = await platform.Enrollments.AsNoTracking().FirstOrDefaultAsync(e => e.UserId == me && e.CourseId == row.CourseId, cancellationToken);
        var roles = await platform.Memberships.AsNoTracking()
            .Where(m => m.UserId == me && m.OrganizationId == row.OrganizationId && m.Status == MembershipStatus.Active)
            .Select(m => m.Roles).FirstOrDefaultAsync(cancellationToken);
        var pending = await platform.Payments.AsNoTracking().AnyAsync(p => p.UserId == me && p.CourseId == row.CourseId && p.Status == PaymentStatus.Pending, cancellationToken);
        var staff = (roles & ~(OrganizationRole.Student | OrganizationRole.Parent)) != OrganizationRole.None;

        // Linked children, so a parent can act for one of them.
        var children = await (from l in platform.ParentLinks.AsNoTracking()
                              join u in platform.Users.AsNoTracking() on l.ChildUserId equals u.Id
                              where l.ParentUserId == me
                              orderby u.FullName
                              select new { u.Id, Name = u.FullName == "" ? u.UserName! : u.FullName }).ToListAsync(cancellationToken);
        var childIds = children.Select(c => c.Id).ToList();
        var childEnrollments = await platform.Enrollments.AsNoTracking()
            .Where(e => childIds.Contains(e.UserId) && e.CourseId == row.CourseId).ToListAsync(cancellationToken);
        var childPending = await platform.Payments.AsNoTracking()
            .Where(p => childIds.Contains(p.UserId) && p.CourseId == row.CourseId && p.Status == PaymentStatus.Pending)
            .Select(p => p.UserId).ToListAsync(cancellationToken);
        var childRows = children.Select(c => new ViewerChildDto(c.Id, c.Name,
            childEnrollments.Any(e => e.UserId == c.Id && e.IsUsable(now)), childPending.Contains(c.Id))).ToList();

        return new CourseViewerDto(true, enrollment?.IsUsable(now) == true, staff, pending,
            MustAskParent: !await learners.MayPayAsync(cancellationToken), Children: childRows);
    }

    private async Task<IReadOnlyList<CourseCardDto>> CardsAsync(IReadOnlyCollection<MarketplaceCourse> rows, CancellationToken cancellationToken)
    {
        var orgIds = rows.Select(r => r.OrganizationId).Distinct().ToList();
        var teacherSlugs = await platform.MarketplaceTeachers.AsNoTracking().Where(t => orgIds.Contains(t.OrganizationId))
            .ToDictionaryAsync(t => t.OrganizationId, t => t.Slug, cancellationToken);
        return rows.Select(c => new CourseCardDto(
            c.PublicSlug, c.Title, c.Subtitle, c.ThumbnailUrl, c.OrganizationName, c.OrganizationType.ToString(), c.TeacherName,
            teacherSlugs.GetValueOrDefault(c.OrganizationId), c.CountryCode, c.Currency, c.IsFree, c.Price, c.CodeActivationOnly,
            c.IsComingSoon, c.LessonCount, c.TotalMinutes, c.FreePreviewCount, c.CurriculumId, c.GradeId, c.SubjectId)).ToList();
    }

    private static TeacherCardDto ToCard(MarketplaceTeacher t) =>
        new(t.Slug, t.Name, t.Headline, t.City, t.CountryCode, t.Gender.ToString(), t.PhotoUrl, t.SubjectIds, t.GradeIds, t.CourseCount);

    private static (int Page, int Size) Paging(MarketplaceSearch search) =>
        (Math.Max(1, search.Page), Math.Clamp(search.PageSize, 1, MaxPageSize));
}
