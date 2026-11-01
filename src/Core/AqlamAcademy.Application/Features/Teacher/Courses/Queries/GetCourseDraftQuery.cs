using AqlamAcademy.Application.Common.Interfaces;
using AqlamAcademy.Application.Common.Models;
using AqlamAcademy.Application.Features.Teacher.Courses.DTOs;
using MediatR;

namespace AqlamAcademy.Application.Features.Teacher.Courses.Queries;

public record GetCourseDraftQuery(Guid Id) : IRequest<Result<CourseDraftDto>>;

public class GetCourseDraftQueryHandler : IRequestHandler<GetCourseDraftQuery, Result<CourseDraftDto>>
{
    private readonly ICourseService _courseService;

    public GetCourseDraftQueryHandler(ICourseService courseService)
    {
        _courseService = courseService;
    }

    public async Task<Result<CourseDraftDto>> Handle(GetCourseDraftQuery request, CancellationToken cancellationToken)
    {
        // The tenant filter only returns courses of the current organization; permissions are checked by the API policy.
        var draft = await _courseService.GetDraftByIdAsync(request.Id, cancellationToken);

        if (draft == null)
            return Result<CourseDraftDto>.NotFound("Course not found.");

        return Result<CourseDraftDto>.Success(draft);
    }
}
