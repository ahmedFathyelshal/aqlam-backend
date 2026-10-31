using AqlamAcademy.Application.Common.Interfaces;
using AqlamAcademy.Application.Common.Models;
using AqlamAcademy.Application.Features.Teacher.Courses.DTOs;
using MediatR;

namespace AqlamAcademy.Application.Features.Teacher.Courses.Queries;

public record GetCourseDraftQuery(Guid Id) : IRequest<Result<CourseDraftDto>>;

public class GetCourseDraftQueryHandler : IRequestHandler<GetCourseDraftQuery, Result<CourseDraftDto>>
{
    private readonly ICourseService _courseService;
    private readonly ICurrentUserService _currentUser;

    public GetCourseDraftQueryHandler(ICourseService courseService, ICurrentUserService currentUser)
    {
        _courseService = courseService;
        _currentUser = currentUser;
    }

    public async Task<Result<CourseDraftDto>> Handle(GetCourseDraftQuery request, CancellationToken cancellationToken)
    {
        // Ownership: only the course's teacher can load its draft.
        var course = await _courseService.GetCourseByIdAsync(request.Id, cancellationToken);
        if (course == null || course.TeacherId != _currentUser.UserId)
            return Result<CourseDraftDto>.NotFound("Course not found.");

        var draft = await _courseService.GetDraftByIdAsync(request.Id, cancellationToken);

        if (draft == null)
            return Result<CourseDraftDto>.NotFound("Course not found.");

        return Result<CourseDraftDto>.Success(draft);
    }
}
