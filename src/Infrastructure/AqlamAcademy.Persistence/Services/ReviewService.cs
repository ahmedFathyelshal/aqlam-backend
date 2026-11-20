using AqlamAcademy.Application.Common.Interfaces;
using AqlamAcademy.Application.Common.Models;
using AqlamAcademy.Application.Features.Marketplace;
using AqlamAcademy.Domain.Common;
using AqlamAcademy.Domain.Learning;
using AqlamAcademy.Domain.Marketplace;
using AqlamAcademy.Domain.Organizations;
using Microsoft.EntityFrameworkCore;

namespace AqlamAcademy.Infrastructure.Persistence.Services;

/// <summary>Reviews of public courses and the top-teachers list built from them (stories 3.7.1 and 3.7.2).</summary>
public class ReviewService(ApplicationDbContext platform, ICurrentUserService currentUser, IAuditLog audit) : IReviewService
{
    private const int PageSize = 10;
    private const int TopListSize = 10;
    /// <summary>Weight of the overall average when smoothing a teacher's score, in reviews.</summary>
    private const double SmoothingWeight = 5;

    public async Task<Result<ReviewsDto>> ListAsync(string courseSlug, int page, CancellationToken cancellationToken)
    {
        var course = await FindCourseAsync(courseSlug, cancellationToken);
        return course is null ? Result<ReviewsDto>.NotFound("Course not found.") : Result<ReviewsDto>.Success(await BuildAsync(course, page, cancellationToken));
    }

    public async Task<Result<ReviewsDto>> SaveMineAsync(string courseSlug, int stars, string? comment, CancellationToken cancellationToken)
    {
        if (stars is < 1 or > 5)
            return Result<ReviewsDto>.Failure(ResultErrorType.Validation, "Choose between 1 and 5 stars.");
        var text = string.IsNullOrWhiteSpace(comment) ? null : comment.Trim();
        if (text is { Length: > CourseReview.MaxCommentLength })
            return Result<ReviewsDto>.Failure(ResultErrorType.Validation, $"The comment is limited to {CourseReview.MaxCommentLength} characters.");

        var course = await FindCourseAsync(courseSlug, cancellationToken);
        if (course is null)
            return Result<ReviewsDto>.NotFound("Course not found.");
        var reason = await WhyNotAsync(course, cancellationToken);
        if (reason is not null)
            return Result<ReviewsDto>.Failure(ResultErrorType.Validation, ReasonText(reason));

        var me = currentUser.UserId;
        var review = await platform.CourseReviews.FirstOrDefaultAsync(r => r.CourseId == course.CourseId && r.UserId == me, cancellationToken);
        if (review is null)
        {
            review = new CourseReview { CourseId = course.CourseId, OrganizationId = course.OrganizationId, UserId = me };
            platform.CourseReviews.Add(review);
        }
        review.Stars = stars;
        review.Comment = text;
        review.UpdatedAtUtc = DateTime.UtcNow;
        await platform.SaveChangesAsync(cancellationToken);
        return Result<ReviewsDto>.Success(await BuildAsync(course, 1, cancellationToken));
    }

    public async Task<Result<ReviewsDto>> DeleteMineAsync(string courseSlug, CancellationToken cancellationToken)
    {
        var course = await FindCourseAsync(courseSlug, cancellationToken);
        if (course is null)
            return Result<ReviewsDto>.NotFound("Course not found.");
        var me = currentUser.UserId;
        var review = await platform.CourseReviews.FirstOrDefaultAsync(r => r.CourseId == course.CourseId && r.UserId == me, cancellationToken);
        if (review is not null)
        {
            platform.CourseReviews.Remove(review);
            await platform.SaveChangesAsync(cancellationToken);
        }
        return Result<ReviewsDto>.Success(await BuildAsync(course, 1, cancellationToken));
    }

    public async Task<IReadOnlyList<TopTeacherDto>> TopTeachersAsync(int subjectId, string? country, CancellationToken cancellationToken)
    {
        var reviews = from r in platform.CourseReviews.AsNoTracking()
                      join c in platform.MarketplaceCourses.AsNoTracking() on r.CourseId equals c.CourseId
                      where !r.IsHidden && c.SubjectId == subjectId
                      select new { c.OrganizationId, c.CountryCode, r.Stars };
        if (!string.IsNullOrWhiteSpace(country))
            reviews = reviews.Where(x => x.CountryCode == country.ToUpperInvariant());

        var groups = await reviews.GroupBy(x => x.OrganizationId)
            .Select(g => new { OrganizationId = g.Key, Count = g.Count(), Sum = g.Sum(x => x.Stars) })
            .ToListAsync(cancellationToken);
        if (groups.Count == 0)
            return [];

        var overall = (double)groups.Sum(g => g.Sum) / groups.Sum(g => g.Count);
        var eligible = groups.Where(g => g.Count >= CourseReview.MinRatingsToShow).ToList();
        var ids = eligible.Select(g => g.OrganizationId).ToList();
        var teachers = await platform.MarketplaceTeachers.AsNoTracking().Where(t => ids.Contains(t.OrganizationId)).ToDictionaryAsync(t => t.OrganizationId, cancellationToken);

        return eligible.Where(g => teachers.ContainsKey(g.OrganizationId))
            .Select(g => new
            {
                g.OrganizationId, g.Count,
                Average = (double)g.Sum / g.Count,
                Score = (g.Sum + SmoothingWeight * overall) / (g.Count + SmoothingWeight)
            })
            .OrderByDescending(x => x.Score).ThenByDescending(x => x.Count).ThenBy(x => teachers[x.OrganizationId].Name)
            .Take(TopListSize)
            .Select((x, i) => new TopTeacherDto(i + 1, Math.Round(x.Average, 1), x.Count, MarketplaceReader.ToCard(teachers[x.OrganizationId])))
            .ToList();
    }

    public async Task<IReadOnlyList<AdminReviewDto>> AdminListAsync(bool? hidden, int skip, int take, CancellationToken cancellationToken)
    {
        var query = platform.CourseReviews.AsNoTracking().AsQueryable();
        if (hidden is { } h)
            query = query.Where(r => r.IsHidden == h);
        var rows = await (from r in query
                          join u in platform.Users.IgnoreQueryFilters().AsNoTracking() on r.UserId equals u.Id
                          join c in platform.MarketplaceCourses.AsNoTracking() on r.CourseId equals c.CourseId into cs
                          from c in cs.DefaultIfEmpty()
                          orderby r.CreatedAtUtc descending
                          select new { r, u.UserName, Title = c == null ? "" : c.Title })
            .Skip(Math.Max(0, skip)).Take(Math.Clamp(take, 1, 100)).ToListAsync(cancellationToken);
        return rows.Select(x => new AdminReviewDto(x.r.Id, x.Title, x.UserName ?? "", x.r.Stars, x.r.Comment, x.r.IsHidden, x.r.HiddenReason, x.r.CreatedAtUtc)).ToList();
    }

    public async Task<Result> AdminSetHiddenAsync(Guid actorUserId, Guid reviewId, bool hidden, string? reason, CancellationToken cancellationToken)
    {
        var review = await platform.CourseReviews.FirstOrDefaultAsync(r => r.Id == reviewId, cancellationToken);
        if (review is null)
            return Result.Failure(["Review not found."]);
        review.IsHidden = hidden;
        review.HiddenReason = hidden ? (reason is { Length: > 300 } ? reason[..300] : reason) : null;
        review.HiddenAtUtc = hidden ? DateTime.UtcNow : null;
        review.HiddenByUserId = hidden ? actorUserId : null;
        await platform.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(hidden ? "review.hidden" : "review.unhidden", actorUserId, review.UserId, review.OrganizationId, review.HiddenReason, cancellationToken);
        return Result.Success();
    }

    // ---------- helpers ----------

    private async Task<ReviewsDto> BuildAsync(MarketplaceCourse course, int page, CancellationToken cancellationToken)
    {
        var visible = platform.CourseReviews.AsNoTracking().Where(r => r.CourseId == course.CourseId && !r.IsHidden);
        var counts = await visible.GroupBy(r => r.Stars).Select(g => new { Stars = g.Key, Count = g.Count() }).ToListAsync(cancellationToken);
        var distribution = Enumerable.Range(1, 5).Select(s => counts.FirstOrDefault(c => c.Stars == s)?.Count ?? 0).ToList();
        var total = distribution.Sum();
        var summary = new RatingSummaryDto(total, Average(distribution), distribution);

        var me = currentUser.IsAuthenticated ? currentUser.UserId : Guid.Empty;
        var rows = await (from r in visible
                          join u in platform.Users.IgnoreQueryFilters().AsNoTracking() on r.UserId equals u.Id
                          orderby r.CreatedAtUtc descending
                          select new { r.Id, r.Stars, r.Comment, r.CreatedAtUtc, r.UserId, u.FullName, u.BirthYear })
            .Skip((Math.Max(1, page) - 1) * PageSize).Take(PageSize).ToListAsync(cancellationToken);
        var now = DateTime.UtcNow;
        var items = rows.Select(x => new ReviewDto(x.Id, x.Stars, x.Comment, DisplayName(x.FullName, x.BirthYear, now), x.CreatedAtUtc, x.UserId == me)).ToList();

        // The viewer's own review is returned even if hidden, so they see what they wrote.
        var mine = me == Guid.Empty ? null : await platform.CourseReviews.AsNoTracking()
            .Where(r => r.CourseId == course.CourseId && r.UserId == me)
            .Select(r => new ReviewDto(r.Id, r.Stars, r.Comment, null, r.CreatedAtUtc, true)).FirstOrDefaultAsync(cancellationToken);
        var reason = await WhyNotAsync(course, cancellationToken);
        return new ReviewsDto(summary, items, total, reason is null, reason, mine);
    }

    internal static double? Average(IReadOnlyList<int> distribution)
    {
        var count = distribution.Sum();
        if (count < CourseReview.MinRatingsToShow)
            return null;
        return Math.Round(distribution.Select((n, i) => (double)n * (i + 1)).Sum() / count, 1);
    }

    private static string? DisplayName(string fullName, int? birthYear, DateTime now)
    {
        // Minors (and accounts with no known age) stay anonymous; adults show a first name only.
        if (birthYear is not { } year || AgeRules.Age(year, now) < AgeRules.AdultAge)
            return null;
        var first = fullName.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return first;
    }

    /// <summary>Null when the viewer may review; otherwise a short reason code.</summary>
    private async Task<string?> WhyNotAsync(MarketplaceCourse course, CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated)
            return "SignIn";
        var me = currentUser.UserId;

        var roles = await platform.Memberships.AsNoTracking()
            .Where(m => m.UserId == me && m.OrganizationId == course.OrganizationId && m.Status == MembershipStatus.Active)
            .Select(m => m.Roles).FirstOrDefaultAsync(cancellationToken);
        if ((roles & ~(OrganizationRole.Student | OrganizationRole.Parent)) != OrganizationRole.None)
            return "Staff";

        var birthYear = await platform.Users.AsNoTracking().Where(u => u.Id == me).Select(u => u.BirthYear).FirstOrDefaultAsync(cancellationToken);
        if (birthYear is { } year && AgeRules.Age(year, DateTime.UtcNow) < AgeRules.MinSelfRegistrationAge)
            return "TooYoung";

        var enrollment = await platform.Enrollments.AsNoTracking()
            .Where(e => e.UserId == me && e.CourseId == course.CourseId && e.Status == EnrollmentStatus.Active)
            .Select(e => new { e.ProgressPercent }).FirstOrDefaultAsync(cancellationToken);
        if (enrollment is null)
            return "NotEnrolled";
        return enrollment.ProgressPercent < CourseReview.MinProgressPercent ? "TooEarly" : null;
    }

    private static string ReasonText(string reason) => reason switch
    {
        "SignIn" => "Sign in to review a course.",
        "NotEnrolled" => "Only students enrolled in the course can review it.",
        "TooEarly" => $"Complete at least {CourseReview.MinProgressPercent}% of the course before reviewing it.",
        "TooYoung" => "Students under 13 cannot post reviews.",
        _ => "Teachers and staff of this academy cannot review its courses."
    };

    private async Task<MarketplaceCourse?> FindCourseAsync(string slug, CancellationToken cancellationToken)
    {
        var bySlug = await platform.MarketplaceCourses.AsNoTracking().FirstOrDefaultAsync(c => c.PublicSlug == slug, cancellationToken);
        if (bySlug is not null)
            return bySlug;
        var code = slug[(slug.LastIndexOf('-') + 1)..].ToLowerInvariant();
        return code.Length == 8 ? await platform.MarketplaceCourses.AsNoTracking().FirstOrDefaultAsync(c => c.ShortCode == code, cancellationToken) : null;
    }
}
