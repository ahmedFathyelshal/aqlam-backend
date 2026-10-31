using AqlamAcademy.Application.Common.Interfaces;
using AqlamAcademy.Application.Common.Models;
using MediatR;

namespace AqlamAcademy.Application.Features.Lookups.Queries;

public record GetCourseStatesQuery : IRequest<Result<IEnumerable<CourseStateDto>>>;

public class GetCourseStatesQueryHandler : IRequestHandler<GetCourseStatesQuery, Result<IEnumerable<CourseStateDto>>>
{
    private readonly ILookupService _lookupService;

    public GetCourseStatesQueryHandler(ILookupService lookupService)
    {
        _lookupService = lookupService;
    }

    public async Task<Result<IEnumerable<CourseStateDto>>> Handle(GetCourseStatesQuery request, CancellationToken cancellationToken)
    {
        var states = await _lookupService.GetCourseStatesAsync(cancellationToken);
        return Result<IEnumerable<CourseStateDto>>.Success(states);
    }
}
