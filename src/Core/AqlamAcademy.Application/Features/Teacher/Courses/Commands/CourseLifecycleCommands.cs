using AqlamAcademy.Application.Common.Interfaces;
using AqlamAcademy.Application.Common.Models;
using AqlamAcademy.Application.Features.Teacher.Courses.DTOs;
using AqlamAcademy.Domain.Enums;
using AqlamAcademy.Domain.Organizations;
using FluentValidation;
using MediatR;

namespace AqlamAcademy.Application.Features.Teacher.Courses.Commands;

public record ListCoursesQuery : IRequest<Result<IReadOnlyList<CourseSummaryDto>>>;

public class ListCoursesQueryHandler(ICourseService courses) : IRequestHandler<ListCoursesQuery, Result<IReadOnlyList<CourseSummaryDto>>>
{
    public async Task<Result<IReadOnlyList<CourseSummaryDto>>> Handle(ListCoursesQuery request, CancellationToken cancellationToken) =>
        Result<IReadOnlyList<CourseSummaryDto>>.Success(await courses.ListAsync(cancellationToken));
}

/// <summary>What still blocks publishing; an empty list means the course is ready.</summary>
public record GetPublishChecklistQuery(Guid Id) : IRequest<Result<IReadOnlyList<string>>>;

public class GetPublishChecklistQueryHandler(ICourseService courses) : IRequestHandler<GetPublishChecklistQuery, Result<IReadOnlyList<string>>>
{
    public async Task<Result<IReadOnlyList<string>>> Handle(GetPublishChecklistQuery request, CancellationToken cancellationToken) =>
        await courses.GetPublishIssuesAsync(request.Id, cancellationToken) is { } issues
            ? Result<IReadOnlyList<string>>.Success(issues)
            : Result<IReadOnlyList<string>>.NotFound("Course not found.");
}

public record ChangeCourseStatusCommand(Guid Id, CourseStatus Status) : IRequest<Result<CourseDraftDto>>;

public class ChangeCourseStatusCommandValidator : AbstractValidator<ChangeCourseStatusCommand>
{
    public ChangeCourseStatusCommandValidator()
    {
        RuleFor(x => x.Status).IsInEnum()
            .NotEqual(CourseStatus.Suspended).WithMessage("Courses can only be suspended by the platform.");
    }
}

public class ChangeCourseStatusCommandHandler(ICourseService courses) : IRequestHandler<ChangeCourseStatusCommand, Result<CourseDraftDto>>
{
    public Task<Result<CourseDraftDto>> Handle(ChangeCourseStatusCommand request, CancellationToken cancellationToken) =>
        courses.ChangeStatusAsync(request.Id, request.Status, cancellationToken);
}

public record SetCourseVisibilityCommand(Guid Id, CourseVisibility Visibility) : IRequest<Result<CourseDraftDto>>;

public class SetCourseVisibilityCommandValidator : AbstractValidator<SetCourseVisibilityCommand>
{
    public SetCourseVisibilityCommandValidator()
    {
        RuleFor(x => x.Visibility).IsInEnum();
    }
}

public class SetCourseVisibilityCommandHandler(ICourseService courses, ICurrentOrganization organization, IOrganizationPolicyReader policies)
    : IRequestHandler<SetCourseVisibilityCommand, Result<CourseDraftDto>>
{
    public async Task<Result<CourseDraftDto>> Handle(SetCourseVisibilityCommand request, CancellationToken cancellationToken)
    {
        if (request.Visibility == CourseVisibility.Public)
        {
            var policy = organization.OrganizationId is { } orgId
                ? await policies.GetPublicPublishingAsync(orgId, cancellationToken)
                : null;

            // Schools are private by default; their admins must enable public publishing first.
            if (policy is null or PublicPublishingPolicy.Disabled)
                return Result<CourseDraftDto>.Failure(ResultErrorType.Forbidden,
                    "This organization does not allow publishing courses to the public marketplace.");
        }

        return await courses.SetVisibilityAsync(request.Id, request.Visibility, cancellationToken);
    }
}
