using AqlamAcademy.Application.Common.Interfaces;
using AqlamAcademy.Application.Common.Models;
using AqlamAcademy.Application.Features.Teacher.Courses.DTOs;
using MediatR;

namespace AqlamAcademy.Application.Features.Teacher.Courses.Commands;

public record UpdateCourseMediaCommand(Guid Id, string? Subtitle, string? ThumbnailUrl, string? PromoVideoUrl) : IRequest<Result<CourseDraftDto>>;

public class UpdateCourseMediaCommandHandler : IRequestHandler<UpdateCourseMediaCommand, Result<CourseDraftDto>>
{
    private readonly ICourseService _courseService;
    private readonly ICurrentUserService _currentUser;

    public UpdateCourseMediaCommandHandler(ICourseService courseService, ICurrentUserService currentUser)
    {
        _courseService = courseService;
        _currentUser = currentUser;
    }

    public async Task<Result<CourseDraftDto>> Handle(UpdateCourseMediaCommand request, CancellationToken cancellationToken)
    {
        var course = await _courseService.GetCourseByIdAsync(request.Id, cancellationToken);

        // Ownership: a teacher may only modify their own courses. Return 404 (not 403) so ids are not confirmed.
        if (course == null || course.TeacherId != _currentUser.UserId)
            return Result<CourseDraftDto>.NotFound("Course not found.");

        await _courseService.UpdateMediaAsync(request.Id, request.Subtitle, request.ThumbnailUrl, request.PromoVideoUrl, cancellationToken);

        return Result<CourseDraftDto>.Success(new CourseDraftDto { Id = course.Id });
    }
}
