using AqlamAcademy.Application.Common.Interfaces;
using AqlamAcademy.Application.Common.Models;
using MediatR;

namespace AqlamAcademy.Application.Features.Lookups.Queries;

public record GetCurriculaQuery(string? CountryCode = null) : IRequest<Result<IEnumerable<CurriculumDto>>>;

public class GetCurriculaQueryHandler : IRequestHandler<GetCurriculaQuery, Result<IEnumerable<CurriculumDto>>>
{
    private readonly ILookupService _lookupService;

    public GetCurriculaQueryHandler(ILookupService lookupService)
    {
        _lookupService = lookupService;
    }

    public async Task<Result<IEnumerable<CurriculumDto>>> Handle(GetCurriculaQuery request, CancellationToken cancellationToken)
    {
        var curricula = await _lookupService.GetCurriculaAsync(request.CountryCode, cancellationToken);
        return Result<IEnumerable<CurriculumDto>>.Success(curricula);
    }
}
