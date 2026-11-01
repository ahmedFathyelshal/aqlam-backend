using AqlamAcademy.Application.Common.Interfaces;
using AqlamAcademy.Application.Common.Models;
using AqlamAcademy.Application.Features.Teacher.Courses.DTOs;
using MediatR;

namespace AqlamAcademy.Application.Features.Teacher.Courses.Commands;

public record UpdateCourseCurriculumCommand(Guid Id, string? SyllabusRequirements, ICollection<CourseChapterDto> Chapters) : IRequest<Result<CourseDraftDto>>;

public class UpdateCourseCurriculumCommandHandler : IRequestHandler<UpdateCourseCurriculumCommand, Result<CourseDraftDto>>
{
    private readonly ICourseService _courseService;

    public UpdateCourseCurriculumCommandHandler(ICourseService courseService)
    {
        _courseService = courseService;
    }

    public async Task<Result<CourseDraftDto>> Handle(UpdateCourseCurriculumCommand request, CancellationToken cancellationToken)
    {
        var course = await _courseService.GetCourseByIdAsync(request.Id, cancellationToken);

        // The tenant filter only returns courses of the current organization; permissions are checked by the API policy.
        if (course == null)
            return Result<CourseDraftDto>.NotFound("Course not found.");

        await _courseService.UpdateCurriculumAsync(request.Id, request.SyllabusRequirements, request.Chapters, cancellationToken);

        return Result<CourseDraftDto>.Success(new CourseDraftDto { Id = course.Id });
    }
}
