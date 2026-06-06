using AqlamAcademy.Application.Common.Interfaces;
using AqlamAcademy.Application.Common.Models;
using MediatR;

namespace AqlamAcademy.Application.Features.Lookups.Queries;

public record GetSubjectsQuery : IRequest<Result<IEnumerable<SubjectDto>>>;

public class GetSubjectsQueryHandler : IRequestHandler<GetSubjectsQuery, Result<IEnumerable<SubjectDto>>>
{
    private readonly ILookupService _lookupService;

    public GetSubjectsQueryHandler(ILookupService lookupService)
    {
        _lookupService = lookupService;
    }

    public async Task<Result<IEnumerable<SubjectDto>>> Handle(GetSubjectsQuery request, CancellationToken cancellationToken)
    {
        var subjects = await _lookupService.GetSubjectsAsync(cancellationToken);
        return Result<IEnumerable<SubjectDto>>.Success(subjects);
    }
}
