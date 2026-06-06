using AqlamAcademy.Application.Common.Interfaces;
using AqlamAcademy.Application.Features.Lookups;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace AqlamAcademy.Infrastructure.Persistence.Services;

public class LookupService : ILookupService
{
    private readonly ApplicationDbContext _context;
    private readonly IMemoryCache _cache;
    private const string CacheKey = "LookupsCache";

    public LookupService(ApplicationDbContext context, IMemoryCache cache)
    {
        _context = context;
        _cache = cache;
    }

    public async Task<IEnumerable<EducationStageDto>> GetEducationStagesAsync(CancellationToken cancellationToken)
    {
        var cacheKey = "EducationStages";
        if (!_cache.TryGetValue(cacheKey, out IEnumerable<EducationStageDto>? stages))
        {
            stages = await _context.EducationStages
                .Where(s => s.IsActive)
                .OrderBy(s => s.OrderIndex)
                .Select(s => new EducationStageDto(s.Id, s.NameEn, s.NameAr, s.OrderIndex))
                .ToListAsync(cancellationToken);

            _cache.Set(cacheKey, stages, TimeSpan.FromHours(24));
        }

        return stages!;
    }

    public async Task<IEnumerable<EducationGradeDto>> GetEducationGradesAsync(int? stageId, CancellationToken cancellationToken)
    {
        var cacheKey = $"EducationGrades_{stageId?.ToString() ?? "All"}";
        
        if (!_cache.TryGetValue(cacheKey, out IEnumerable<EducationGradeDto>? grades))
        {
            var query = _context.EducationGrades.Where(g => g.IsActive);
            
            if (stageId.HasValue)
            {
                query = query.Where(g => g.EducationStageId == stageId.Value);
            }

            grades = await query
                .OrderBy(g => g.OrderIndex)
                .Select(g => new EducationGradeDto(g.Id, g.EducationStageId, g.NameEn, g.NameAr, g.OrderIndex))
                .ToListAsync(cancellationToken);

            _cache.Set(cacheKey, grades, TimeSpan.FromHours(24));
        }

        return grades!;
    }

    public async Task<IEnumerable<SubjectDto>> GetSubjectsAsync(CancellationToken cancellationToken)
    {
        var cacheKey = "Subjects";
        if (!_cache.TryGetValue(cacheKey, out IEnumerable<SubjectDto>? subjects))
        {
            subjects = await _context.Subjects
                .Where(s => s.IsActive)
                .OrderBy(s => s.OrderIndex)
                .Select(s => new SubjectDto(s.Id, s.NameEn, s.NameAr, s.OrderIndex))
                .ToListAsync(cancellationToken);

            _cache.Set(cacheKey, subjects, TimeSpan.FromHours(24));
        }

        return subjects!;
    }
}
