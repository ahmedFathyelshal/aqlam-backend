using AqlamAcademy.Application.Common.Models;

namespace AqlamAcademy.Application.Features.Marketplace;

/// <summary>Count and average of a course's (or a teacher's) reviews. The average is null until there are enough reviews.</summary>
public record RatingSummaryDto(int Count, double? Average, IReadOnlyList<int> Distribution);

/// <summary>Reviewer is the first name of an adult student, and null (shown as "Student") for a minor or when no name is set.</summary>
public record ReviewDto(Guid Id, int Stars, string? Comment, string? Reviewer, DateTime CreatedAtUtc, bool Mine);

public record ReviewsDto(
    RatingSummaryDto Summary,
    IReadOnlyList<ReviewDto> Items,
    int Total,
    bool CanReview,
    /// <summary>When the viewer cannot review: SignIn, NotEnrolled, TooEarly, TooYoung or Staff.</summary>
    string? CannotReviewReason,
    ReviewDto? Mine);

/// <summary>One place in a subject's top list. Only teachers with enough reviews appear.</summary>
public record TopTeacherDto(int Rank, double Average, int Count, TeacherCardDto Teacher);

public record AdminReviewDto(Guid Id, string CourseTitle, string UserName, int Stars, string? Comment, bool IsHidden, string? HiddenReason, DateTime CreatedAtUtc);

public interface IReviewService
{
    Task<Result<ReviewsDto>> ListAsync(string courseSlug, int page, CancellationToken cancellationToken);
    Task<Result<ReviewsDto>> SaveMineAsync(string courseSlug, int stars, string? comment, CancellationToken cancellationToken);
    Task<Result<ReviewsDto>> DeleteMineAsync(string courseSlug, CancellationToken cancellationToken);

    /// <summary>
    /// Independent teachers of a subject, best first. Ranked by a smoothed average (so a few five-star votes do not
    /// beat many good ones) and listed only with enough reviews. Never paid and never hand-picked.
    /// </summary>
    Task<IReadOnlyList<TopTeacherDto>> TopTeachersAsync(int subjectId, string? country, CancellationToken cancellationToken);

    Task<IReadOnlyList<AdminReviewDto>> AdminListAsync(bool? hidden, int skip, int take, CancellationToken cancellationToken);
    Task<Result> AdminSetHiddenAsync(Guid actorUserId, Guid reviewId, bool hidden, string? reason, CancellationToken cancellationToken);
}
