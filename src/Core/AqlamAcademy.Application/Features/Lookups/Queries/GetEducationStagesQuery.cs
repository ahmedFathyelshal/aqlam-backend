using AqlamAcademy.Application.Common.Interfaces;
using AqlamAcademy.Application.Common.Models;
using MediatR;

namespace AqlamAcademy.Application.Features.Lookups.Queries;

public record GetEducationStagesQuery(int? CurriculumId = null) : IRequest<Result<IEnumerable<EducationStageDto>>>;

public class GetEducationStagesQueryHandler : IRequestHandler<GetEducationStagesQuery, Result<IEnumerable<EducationStageDto>>>
{
    private readonly ILookupService _lookupService;

    public GetEducationStagesQueryHandler(ILookupService lookupService)
    {
        _lookupService = lookupService;
    }

    public async Task<Result<IEnumerable<EducationStageDto>>> Handle(GetEducationStagesQuery request, CancellationToken cancellationToken)
    {
        var stages = await _lookupService.GetEducationStagesAsync(request.CurriculumId, cancellationToken);
        return Result<IEnumerable<EducationStageDto>>.Success(stages);
    }
}
