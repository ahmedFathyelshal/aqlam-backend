using AqlamAcademy.Application.Common.Interfaces;
using AqlamAcademy.Application.Common.Models;
using AqlamAcademy.Application.Features.Teacher.Courses.DTOs;
using AqlamAcademy.Domain.Courses;
using AqlamAcademy.Domain.Enums;
using MediatR;

namespace AqlamAcademy.Application.Features.Teacher.Courses.Commands;

public class InitializeCourseDraftCommand : IRequest<Result<CourseDraftDto>>
{
    public string Title { get; set; } = string.Empty;
    public int StageId { get; set; }
    public int GradeId { get; set; }
    public int SubjectId { get; set; }
}

public class InitializeCourseDraftCommandHandler : IRequestHandler<InitializeCourseDraftCommand, Result<CourseDraftDto>>
{
    private readonly ICourseService _courseService;
    private readonly ICurrentUserService _currentUser;

    public InitializeCourseDraftCommandHandler(ICourseService courseService, ICurrentUserService currentUser)
    {
        _courseService = courseService;
        _currentUser = currentUser;
    }

    public async Task<Result<CourseDraftDto>> Handle(InitializeCourseDraftCommand request, CancellationToken cancellationToken)
    {
        var course = new Course
        {
            Title = request.Title,
            StageId = request.StageId,
            GradeId = request.GradeId,
            SubjectId = request.SubjectId,
            Slug = Guid.NewGuid().ToString("N"),
            Status = CourseStatus.Draft,
            TeacherId = _currentUser.UserId
        };

        var draft = await _courseService.CreateDraftAsync(course, cancellationToken);

        return Result<CourseDraftDto>.Success(draft);
    }
}
