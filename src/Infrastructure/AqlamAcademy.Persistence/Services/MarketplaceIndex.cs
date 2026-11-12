using System.Text.RegularExpressions;
using AqlamAcademy.Domain.Classes;
using AqlamAcademy.Domain.Courses;
using AqlamAcademy.Domain.Enums;
using AqlamAcademy.Domain.Marketplace;
using AqlamAcademy.Domain.Organizations;
using AqlamAcademy.Domain.Staff;
using AqlamAcademy.Infrastructure.Persistence.MultiTenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AqlamAcademy.Infrastructure.Persistence.Services;

/// <summary>
/// Keeps the marketplace copy (<see cref="MarketplaceCourse"/>, <see cref="MarketplaceTeacher"/>) in step with
/// courses, organizations and teacher profiles. A course is listed only when it is public, published or scheduled,
/// and belongs to an active personal academy or center whose publishing policy allows it. Schools never appear.
/// </summary>
public partial class MarketplaceIndex(ApplicationDbContext platform, TenantStores tenants, ILogger<MarketplaceIndex> logger)
{
    public static bool CanList(Organization org) =>
        org.Status == OrganizationStatus.Active
        && org.Type is OrganizationType.Personal or OrganizationType.Center
        && org.PublicPublishing == PublicPublishingPolicy.Allowed;

    public static bool CanList(Course course) =>
        !course.IsDeleted
        && course.Visibility == CourseVisibility.Public
        && course.Status is CourseStatus.Published or CourseStatus.Scheduled;

    /// <summary>Call after a course was saved, with the tenant store it was saved in.</summary>
    public async Task RefreshCourseAsync(Guid orgId, Guid courseId, TenantDbContext tenant, CancellationToken cancellationToken)
    {
        var org = await platform.Organizations.AsNoTracking().FirstOrDefaultAsync(o => o.Id == orgId, cancellationToken);
        var course = await LoadCourseAsync(tenant, courseId, cancellationToken);
        await WriteCourseAsync(org, course, courseId, cancellationToken);
        await platform.SaveChangesAsync(cancellationToken);
        if (org is { Type: OrganizationType.Personal })
            await RefreshTeacherAsync(orgId, cancellationToken);
    }

    /// <summary>Re-reads every course of the organization (start-up, policy or status changes).</summary>
    public async Task RefreshOrganizationAsync(Guid orgId, CancellationToken cancellationToken)
    {
        var org = await platform.Organizations.AsNoTracking().FirstOrDefaultAsync(o => o.Id == orgId, cancellationToken);
        if (org is null)
            return;

        var existing = await platform.MarketplaceCourses.Where(c => c.OrganizationId == orgId).ToListAsync(cancellationToken);
        if (!CanList(org))
        {
            platform.MarketplaceCourses.RemoveRange(existing);
        }
        else
        {
            await using var tenant = await tenants.OpenAsync(orgId, cancellationToken);
            if (tenant is null)
                return;
            var ids = await tenant.Courses.AsNoTracking()
                .Where(c => c.Visibility == CourseVisibility.Public && (c.Status == CourseStatus.Published || c.Status == CourseStatus.Scheduled))
                .Select(c => c.Id).ToListAsync(cancellationToken);
            platform.MarketplaceCourses.RemoveRange(existing.Where(e => !ids.Contains(e.CourseId)));
            foreach (var id in ids)
                await WriteCourseAsync(org, await LoadCourseAsync(tenant, id, cancellationToken), id, cancellationToken);
        }
        await platform.SaveChangesAsync(cancellationToken);
        await RefreshTeacherAsync(orgId, cancellationToken);
    }

    /// <summary>Start-up: lists every course that should be listed (and nothing else). Failures are logged per organization.</summary>
    public async Task RebuildAllAsync(CancellationToken cancellationToken)
    {
        var orgIds = await platform.Organizations.AsNoTracking()
            .Where(o => o.Type == OrganizationType.Personal || o.Type == OrganizationType.Center
                        || platform.MarketplaceCourses.Any(c => c.OrganizationId == o.Id))
            .Select(o => o.Id).ToListAsync(cancellationToken);
        foreach (var orgId in orgIds)
        {
            try
            {
                await RefreshOrganizationAsync(orgId, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Could not refresh the marketplace listing of organization {OrganizationId}.", orgId);
                platform.ChangeTracker.Clear();
            }
        }
    }

    /// <summary>
    /// The teacher's row in teacher search (personal academies only), and the teacher and organization names on
    /// every course row of the organization. Call after a profile, name or gender change.
    /// </summary>
    public async Task RefreshTeacherAsync(Guid orgId, CancellationToken cancellationToken)
    {
        var org = await platform.Organizations.AsNoTracking().FirstOrDefaultAsync(o => o.Id == orgId, cancellationToken);
        if (org is null)
            return;

        var courses = await platform.MarketplaceCourses.Where(c => c.OrganizationId == orgId).ToListAsync(cancellationToken);
        var teacherIds = courses.Select(c => c.TeacherUserId).ToHashSet();
        var ownerId = org.Type == OrganizationType.Personal ? await OwnerOfAsync(orgId, cancellationToken) : null;
        if (ownerId is { } owner)
            teacherIds.Add(owner);
        var people = await PeopleAsync(teacherIds, cancellationToken);

        foreach (var course in courses)
        {
            var person = people.GetValueOrDefault(course.TeacherUserId);
            course.OrganizationName = org.Name;
            course.OrganizationSlug = org.Slug;
            course.TeacherName = PublicNameOf(person, org);
            course.TeacherGender = person?.Gender;
            course.ProviderSearchText = SearchText.Build(course.TeacherName, org.Name);
        }

        var row = await platform.MarketplaceTeachers.FirstOrDefaultAsync(t => t.OrganizationId == orgId, cancellationToken);
        var profile = ownerId is { } id ? people.GetValueOrDefault(id) : null;
        var listed = ownerId is not null && CanList(org) && profile is { Profile.IsListed: true, Gender: not null }
                     && !string.IsNullOrWhiteSpace(profile.Profile.PublicName);
        if (!listed)
        {
            if (row is not null)
                platform.MarketplaceTeachers.Remove(row);
        }
        else
        {
            if (row is null)
            {
                row = new MarketplaceTeacher { OrganizationId = orgId };
                platform.MarketplaceTeachers.Add(row);
            }
            var p = profile!.Profile!;
            row.Slug = org.Slug;
            row.TeacherUserId = ownerId!.Value;
            row.Name = p.PublicName!.Trim();
            row.Headline = p.Headline;
            row.City = p.City;
            row.CountryCode = org.CountryCode;
            row.Gender = profile.Gender!.Value;
            row.PhotoUrl = p.PhotoUpdatedAtUtc is { } at ? TeacherPhoto.PublicPath(org.Slug, at) : null;
            row.SubjectIds = courses.Where(c => c.SubjectId != null).Select(c => c.SubjectId!.Value).Distinct().Order().ToArray();
            row.GradeIds = courses.Where(c => c.GradeId != null).Select(c => c.GradeId!.Value).Distinct().Order().ToArray();
            row.CurriculumIds = courses.Where(c => c.CurriculumId != null).Select(c => c.CurriculumId!.Value).Distinct().Order().ToArray();
            row.CourseCount = courses.Count;
            row.SearchText = SearchText.Build([row.Name, row.Headline, row.City, .. await LookupWordsAsync(row.SubjectIds, row.GradeIds, [], cancellationToken)]);
            row.UpdatedAtUtc = DateTime.UtcNow;
        }
        await platform.SaveChangesAsync(cancellationToken);
    }

    private async Task WriteCourseAsync(Organization? org, Course? course, Guid courseId, CancellationToken cancellationToken)
    {
        var row = await platform.MarketplaceCourses.FirstOrDefaultAsync(c => c.CourseId == courseId, cancellationToken);
        if (org is null || course is null || !CanList(org) || !CanList(course))
        {
            if (row is not null)
                platform.MarketplaceCourses.Remove(row);
            return;
        }

        var now = DateTime.UtcNow;
        if (row is null)
        {
            row = new MarketplaceCourse { CourseId = courseId, ShortCode = courseId.ToString("N")[..8], PublishedAtUtc = now };
            platform.MarketplaceCourses.Add(row);
        }

        var teacherId = org.Type == OrganizationType.Personal ? await OwnerOfAsync(org.Id, cancellationToken) ?? course.CreatedByUserId : course.CreatedByUserId;
        var person = (await PeopleAsync([teacherId], cancellationToken)).GetValueOrDefault(teacherId);
        var lessons = course.Chapters.Where(c => !c.IsDeleted).SelectMany(c => c.Lessons).Where(l => !l.IsDeleted && l.IsPublished).ToList();

        row.PublicSlug = $"{Slugify(course.Title)}-{row.ShortCode}";
        row.OrganizationId = org.Id;
        row.OrganizationSlug = org.Slug;
        row.OrganizationName = org.Name;
        row.OrganizationType = org.Type;
        row.CountryCode = org.CountryCode;
        row.Currency = org.Currency;
        row.TeacherUserId = teacherId;
        row.TeacherName = PublicNameOf(person, org);
        row.TeacherGender = person?.Gender;
        row.Title = course.Title;
        row.Subtitle = course.Subtitle;
        row.ThumbnailUrl = course.ThumbnailUrl;
        row.HasPromoVideo = !string.IsNullOrWhiteSpace(course.PromoVideoUrl);
        row.CurriculumId = course.CurriculumId;
        row.StageId = course.StageId;
        row.GradeId = course.GradeId;
        row.SubjectId = course.SubjectId;
        row.IsFree = course.PricingType != PricingType.Paid;
        row.Price = course.PricingType == PricingType.Paid ? course.Price : null;
        row.CodeActivationOnly = course.CodeActivationOnly;
        row.IsComingSoon = course.Status == CourseStatus.Scheduled;
        row.LessonCount = lessons.Count;
        row.TotalMinutes = lessons.Sum(l => l.DurationInMinutes ?? 0);
        row.FreePreviewCount = lessons.Count(l => l.IsFreePreview);
        row.SearchText = SearchText.Build([course.Title, course.Subtitle, course.CustomSubjectName,
            .. await LookupWordsAsync(course.SubjectId is { } s ? [s] : [], course.GradeId is { } g ? [g] : [], course.CurriculumId is { } c ? [c] : [], cancellationToken)]);
        row.ProviderSearchText = SearchText.Build(row.TeacherName, org.Name);
        row.UpdatedAtUtc = now;
    }

    private static Task<Course?> LoadCourseAsync(TenantDbContext tenant, Guid courseId, CancellationToken cancellationToken) =>
        tenant.Courses.AsNoTracking()
            .Include(c => c.Chapters).ThenInclude(ch => ch.Lessons)
            .AsSplitQuery()
            .FirstOrDefaultAsync(c => c.Id == courseId, cancellationToken);

    private Task<Guid?> OwnerOfAsync(Guid orgId, CancellationToken cancellationToken) =>
        platform.Memberships.AsNoTracking()
            .Where(m => m.OrganizationId == orgId && m.Status == MembershipStatus.Active && (m.Roles & OrganizationRole.Owner) == OrganizationRole.Owner)
            .OrderBy(m => m.JoinedAtUtc)
            .Select(m => (Guid?)m.UserId)
            .FirstOrDefaultAsync(cancellationToken);

    private record Person(string FullName, string UserName, Gender? Gender, TeacherProfile? Profile);

    private async Task<Dictionary<Guid, Person>> PeopleAsync(IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken) =>
        await (from u in platform.Users.AsNoTracking()
               where userIds.Contains(u.Id)
               join p in platform.TeacherProfiles.AsNoTracking() on u.Id equals p.UserId into profiles
               from p in profiles.DefaultIfEmpty()
               select new { u.Id, u.FullName, u.UserName, u.Gender, Profile = p })
            .ToDictionaryAsync(x => x.Id, x => new Person(x.FullName, x.UserName ?? string.Empty, x.Gender, x.Profile), cancellationToken);

    /// <summary>
    /// The public name, else the full name. Usernames are never shown publicly (a personal academy is named after the
    /// username until the teacher renames it), so with nothing better the name stays empty and the page says
    /// "independent teacher". A center's courses fall back to the center's name.
    /// </summary>
    private static string PublicNameOf(Person? person, Organization org)
    {
        if (!string.IsNullOrWhiteSpace(person?.Profile?.PublicName))
            return person.Profile.PublicName.Trim();
        if (!string.IsNullOrWhiteSpace(person?.FullName) && !IsUserName(person.FullName, person))
            return person.FullName.Trim();
        if (org.Type == OrganizationType.Center)
            return org.Name;
        return IsUserName(org.Name, person) || org.Name.Equals(org.Slug, StringComparison.OrdinalIgnoreCase) ? string.Empty : org.Name;
    }

    private static bool IsUserName(string name, Person? person) =>
        person is not null && name.Trim().Equals(person.UserName, StringComparison.OrdinalIgnoreCase);

    /// <summary>Arabic and English names of the subjects, grades and curricula, so "رياضيات" or "Qudrat" find the course.</summary>
    private async Task<List<string>> LookupWordsAsync(int[] subjectIds, int[] gradeIds, int[] curriculumIds, CancellationToken cancellationToken)
    {
        var words = new List<string>();
        if (subjectIds.Length > 0)
            words.AddRange((await platform.Subjects.AsNoTracking().Where(s => subjectIds.Contains(s.Id)).Select(s => new[] { s.NameAr, s.NameEn }).ToListAsync(cancellationToken)).SelectMany(x => x));
        if (gradeIds.Length > 0)
            words.AddRange((await platform.EducationGrades.AsNoTracking().Where(g => gradeIds.Contains(g.Id)).Select(g => new[] { g.NameAr, g.NameEn }).ToListAsync(cancellationToken)).SelectMany(x => x));
        if (curriculumIds.Length > 0)
            words.AddRange((await platform.Curricula.AsNoTracking().Where(c => curriculumIds.Contains(c.Id)).Select(c => new[] { c.NameAr, c.NameEn }).ToListAsync(cancellationToken)).SelectMany(x => x));
        return words;
    }

    /// <summary>Lower-case words joined by dashes; Arabic letters are kept (they read well in Arabic search results).</summary>
    public static string Slugify(string title)
    {
        // Diacritics and tatweel are dropped first so they do not split words.
        var slug = NotWord().Replace(Marks().Replace(title.Trim().ToLowerInvariant(), string.Empty), "-").Trim('-');
        if (slug.Length > 80)
            slug = slug[..80].Trim('-');
        return slug.Length == 0 ? "course" : slug;
    }

    [GeneratedRegex(@"[^\p{L}\p{Nd}]+")]
    private static partial Regex NotWord();

    [GeneratedRegex(@"[\p{Mn}ـ]")]
    private static partial Regex Marks();
}
