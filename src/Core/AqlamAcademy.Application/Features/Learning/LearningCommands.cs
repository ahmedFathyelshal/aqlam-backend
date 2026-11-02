using AqlamAcademy.Application.Common.Models;
using FluentValidation;
using MediatR;

namespace AqlamAcademy.Application.Features.Learning;

public record GetLearnerCourseQuery(Guid CourseId) : IRequest<Result<LearnerCourseDto>>;

public class GetLearnerCourseQueryHandler(ILearningService learning) : IRequestHandler<GetLearnerCourseQuery, Result<LearnerCourseDto>>
{
    public Task<Result<LearnerCourseDto>> Handle(GetLearnerCourseQuery request, CancellationToken cancellationToken) =>
        learning.GetCourseAsync(request.CourseId, cancellationToken);
}

public record GetLessonQuery(Guid LessonId) : IRequest<Result<LessonContentDto>>;

public class GetLessonQueryHandler(ILearningService learning) : IRequestHandler<GetLessonQuery, Result<LessonContentDto>>
{
    public Task<Result<LessonContentDto>> Handle(GetLessonQuery request, CancellationToken cancellationToken) =>
        learning.GetLessonAsync(request.LessonId, cancellationToken);
}

/// <summary>
/// Heartbeat from the player. Videos complete at 90% watched; articles complete when the student marks them
/// done; quizzes and assignments complete through their submissions, not through this endpoint.
/// </summary>
public record RecordProgressCommand(Guid LessonId, int? PositionSeconds, int? WatchedPercent, bool MarkCompleted = false)
    : IRequest<Result<ProgressResultDto>>;

public class RecordProgressCommandValidator : AbstractValidator<RecordProgressCommand>
{
    public RecordProgressCommandValidator()
    {
        RuleFor(x => x.PositionSeconds).GreaterThanOrEqualTo(0).When(x => x.PositionSeconds.HasValue);
        RuleFor(x => x.WatchedPercent).InclusiveBetween(0, 100).When(x => x.WatchedPercent.HasValue);
    }
}

public class RecordProgressCommandHandler(ILearningService learning) : IRequestHandler<RecordProgressCommand, Result<ProgressResultDto>>
{
    public Task<Result<ProgressResultDto>> Handle(RecordProgressCommand request, CancellationToken cancellationToken) =>
        learning.RecordProgressAsync(request.LessonId, request.PositionSeconds, request.WatchedPercent, request.MarkCompleted, cancellationToken);
}

/// <summary>Student enrolls in a free, published course that is public or unlisted.</summary>
public record EnrollSelfCommand(Guid CourseId) : IRequest<Result<MyCourseDto>>;

public class EnrollSelfCommandHandler(ILearningService learning) : IRequestHandler<EnrollSelfCommand, Result<MyCourseDto>>
{
    public Task<Result<MyCourseDto>> Handle(EnrollSelfCommand request, CancellationToken cancellationToken) =>
        learning.EnrollSelfAsync(request.CourseId, cancellationToken);
}

/// <summary>Staff enroll existing members of the organization (e.g. a class) into a course.</summary>
public record EnrollMembersCommand(Guid CourseId, IReadOnlyList<string> UserNames) : IRequest<Result<IReadOnlyList<EnrollMemberResultDto>>>;

public class EnrollMembersCommandValidator : AbstractValidator<EnrollMembersCommand>
{
    public EnrollMembersCommandValidator()
    {
        RuleFor(x => x.UserNames).NotEmpty().Must(u => u.Count <= 500).WithMessage("At most 500 students per request.");
        RuleForEach(x => x.UserNames).NotEmpty();
    }
}

public class EnrollMembersCommandHandler(ILearningService learning) : IRequestHandler<EnrollMembersCommand, Result<IReadOnlyList<EnrollMemberResultDto>>>
{
    public Task<Result<IReadOnlyList<EnrollMemberResultDto>>> Handle(EnrollMembersCommand request, CancellationToken cancellationToken) =>
        learning.EnrollMembersAsync(request.CourseId, request.UserNames, cancellationToken);
}

public record ListEnrollmentsQuery(Guid CourseId) : IRequest<Result<IReadOnlyList<EnrollmentDto>>>;

public class ListEnrollmentsQueryHandler(ILearningService learning) : IRequestHandler<ListEnrollmentsQuery, Result<IReadOnlyList<EnrollmentDto>>>
{
    public Task<Result<IReadOnlyList<EnrollmentDto>>> Handle(ListEnrollmentsQuery request, CancellationToken cancellationToken) =>
        learning.ListEnrollmentsAsync(request.CourseId, cancellationToken);
}

public record GetMyCoursesQuery : IRequest<Result<IReadOnlyList<MyCourseDto>>>;

public class GetMyCoursesQueryHandler(ILearningService learning) : IRequestHandler<GetMyCoursesQuery, Result<IReadOnlyList<MyCourseDto>>>
{
    public async Task<Result<IReadOnlyList<MyCourseDto>>> Handle(GetMyCoursesQuery request, CancellationToken cancellationToken) =>
        Result<IReadOnlyList<MyCourseDto>>.Success(await learning.GetMyCoursesAsync(cancellationToken));
}
