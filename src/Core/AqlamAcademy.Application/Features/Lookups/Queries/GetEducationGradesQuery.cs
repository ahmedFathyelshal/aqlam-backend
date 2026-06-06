using AqlamAcademy.Application.Common.Interfaces;
using AqlamAcademy.Application.Common.Models;
using MediatR;

namespace AqlamAcademy.Application.Features.Lookups.Queries;

public record GetEducationGradesQuery(int? StageId = null) : IRequest<Result<IEnumerable<EducationGradeDto>>>;

public class GetEducationGradesQueryHandler : IRequestHandler<GetEducationGradesQuery, Result<IEnumerable<EducationGradeDto>>>
{
    private readonly ILookupService _lookupService;

    public GetEducationGradesQueryHandler(ILookupService lookupService)
    {
        _lookupService = lookupService;
    }

    public async Task<Result<IEnumerable<EducationGradeDto>>> Handle(GetEducationGradesQuery request, CancellationToken cancellationToken)
    {
        var grades = await _lookupService.GetEducationGradesAsync(request.StageId, cancellationToken);
        return Result<IEnumerable<EducationGradeDto>>.Success(grades);
    }
}
