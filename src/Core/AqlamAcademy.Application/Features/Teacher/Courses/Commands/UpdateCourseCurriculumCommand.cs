using AqlamAcademy.Application.Common.Interfaces;
using AqlamAcademy.Application.Common.Models;
using AqlamAcademy.Application.Features.Teacher.Courses.DTOs;
using MediatR;

namespace AqlamAcademy.Application.Features.Teacher.Courses.Commands;

public record UpdateCourseCurriculumCommand(Guid Id, string? SyllabusRequirements, ICollection<CourseChapterDto> Chapters) : IRequest<Result<CourseDraftDto>>;

public class UpdateCourseCurriculumCommandHandler : IRequestHandler<UpdateCourseCurriculumCommand, Result<CourseDraftDto>>
{
    private readonly ICourseService _courseService;
    private readonly ICurrentUserService _currentUser;

    public UpdateCourseCurriculumCommandHandler(ICourseService courseService, ICurrentUserService currentUser)
    {
        _courseService = courseService;
        _currentUser = currentUser;
    }

    public async Task<Result<CourseDraftDto>> Handle(UpdateCourseCurriculumCommand request, CancellationToken cancellationToken)
    {
        var course = await _courseService.GetCourseByIdAsync(request.Id, cancellationToken);

        // Ownership: a teacher may only modify their own courses. Return 404 (not 403) so ids are not confirmed.
        if (course == null || course.TeacherId != _currentUser.UserId)
            return Result<CourseDraftDto>.NotFound("Course not found.");

        await _courseService.UpdateCurriculumAsync(request.Id, request.SyllabusRequirements, request.Chapters, cancellationToken);

        return Result<CourseDraftDto>.Success(new CourseDraftDto { Id = course.Id });
    }
}
