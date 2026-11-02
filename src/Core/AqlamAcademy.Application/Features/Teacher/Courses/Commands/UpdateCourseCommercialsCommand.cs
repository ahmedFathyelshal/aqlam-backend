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

    public UpdateCourseCommercialsCommandHandler(ICourseService courseService)
    {
        _courseService = courseService;
    }

    public async Task<Result<CourseDraftDto>> Handle(UpdateCourseCommercialsCommand request, CancellationToken cancellationToken)
    {
        var course = await _courseService.GetCourseByIdAsync(request.Id, cancellationToken);

        // The tenant filter only returns courses of the current organization; permissions are checked by the API policy.
        if (course == null)
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
