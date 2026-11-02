using AqlamAcademy.Application.Common.Interfaces;
using AqlamAcademy.Application.Common.Models;
using AqlamAcademy.Application.Features.Teacher.Courses.DTOs;
using AqlamAcademy.Domain.Enums;
using MediatR;

namespace AqlamAcademy.Application.Features.Teacher.Courses.Commands;

public record UpdateCourseCommercialsCommand(Guid Id, string PricingType, decimal? Price, bool CodeActivationOnly, string AccessExpiryPolicy, int? AccessDurationDays) : IRequest<Result<CourseDraftDto>>;

public class UpdateCourseCommercialsCommandHandler : IRequestHandler<UpdateCourseCommercialsCommand, Result<CourseDraftDto>>
{
    private readonly ICourseService _courseService;
    private readonly ICurrentUserService _currentUser;

    public UpdateCourseCommercialsCommandHandler(ICourseService courseService, ICurrentUserService currentUser)
    {
        _courseService = courseService;
        _currentUser = currentUser;
    }

    public async Task<Result<CourseDraftDto>> Handle(UpdateCourseCommercialsCommand request, CancellationToken cancellationToken)
    {
        var course = await _courseService.GetCourseByIdAsync(request.Id, cancellationToken);

        // Ownership: a teacher may only modify their own courses. Return 404 (not 403) so ids are not confirmed.
        if (course == null || course.TeacherId != _currentUser.UserId)
            return Result<CourseDraftDto>.NotFound("Course not found.");

        // Parse enums in Application layer (business validation)
        PricingType? parsedPricingType = null;
        if (Enum.TryParse<PricingType>(request.PricingType, out var pt))
            parsedPricingType = pt;

        AccessExpiryPolicy? parsedExpiryPolicy = null;
        if (Enum.TryParse<AccessExpiryPolicy>(request.AccessExpiryPolicy, out var ep))
            parsedExpiryPolicy = ep;

        await _courseService.UpdateCommercialsAsync(
            request.Id,
            parsedPricingType,
            request.Price,
            request.CodeActivationOnly,
            parsedExpiryPolicy,
            request.AccessDurationDays,
            cancellationToken);

        return Result<CourseDraftDto>.Success(new CourseDraftDto { Id = course.Id });
    }
}
