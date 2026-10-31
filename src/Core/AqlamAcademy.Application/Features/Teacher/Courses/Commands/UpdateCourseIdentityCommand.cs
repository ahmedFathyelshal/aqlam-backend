using AqlamAcademy.Application.Common.Interfaces;
using AqlamAcademy.Application.Common.Models;
using AqlamAcademy.Application.Features.Teacher.Courses.DTOs;
using MediatR;

namespace AqlamAcademy.Application.Features.Teacher.Courses.Commands;

public record UpdateCourseIdentityCommand(Guid Id, string Title, int? StageId, int? GradeId, int? SubjectId) : IRequest<Result<CourseDraftDto>>;

public class UpdateCourseIdentityCommandHandler : IRequestHandler<UpdateCourseIdentityCommand, Result<CourseDraftDto>>
{
    private readonly ICourseService _courseService;

    public UpdateCourseIdentityCommandHandler(ICourseService courseService)
    {
        _courseService = courseService;
    }

    public async Task<Result<CourseDraftDto>> Handle(UpdateCourseIdentityCommand request, CancellationToken cancellationToken)
    {
        var course = await _courseService.GetCourseByIdAsync(request.Id, cancellationToken);

        if (course == null)
            return Result<CourseDraftDto>.Failure(new[] { "Course not found." });

        await _courseService.UpdateIdentityAsync(request.Id, request.Title, request.StageId, request.GradeId, request.SubjectId, cancellationToken);

        return Result<CourseDraftDto>.Success(new CourseDraftDto { Id = course.Id });
    }
}
