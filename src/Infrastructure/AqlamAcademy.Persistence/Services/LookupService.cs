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

    public async Task<IEnumerable<CurriculumDto>> GetCurriculaAsync(string? countryCode, CancellationToken cancellationToken)
    {
        var country = string.IsNullOrWhiteSpace(countryCode) ? null : countryCode.Trim().ToUpperInvariant();
        var cacheKey = $"Curricula_{country ?? "All"}";

        if (!_cache.TryGetValue(cacheKey, out IEnumerable<CurriculumDto>? curricula))
        {
            var query = _context.Curricula.Where(c => c.IsActive);
            if (country is not null)
                query = query.Where(c => c.CountryCode == null || c.CountryCode == country);

            curricula = (await query
                    .OrderBy(c => c.CountryCode == null ? 1 : 0) // national curricula first
                    .ThenBy(c => c.OrderIndex)
                    .ToListAsync(cancellationToken))
                .Select(c => new CurriculumDto(c.Id, c.Code, c.NameEn, c.NameAr, c.CountryCode, c.Kind.ToString(), c.OrderIndex))
                .ToList();

            _cache.Set(cacheKey, curricula, TimeSpan.FromHours(24));
        }

        return curricula!;
    }

    public async Task<IEnumerable<EducationStageDto>> GetEducationStagesAsync(int? curriculumId, CancellationToken cancellationToken)
    {
        var cacheKey = $"EducationStages_{curriculumId?.ToString() ?? "All"}";
        if (!_cache.TryGetValue(cacheKey, out IEnumerable<EducationStageDto>? stages))
        {
            var query = _context.EducationStages.Where(s => s.IsActive);
            if (curriculumId.HasValue)
                query = query.Where(s => s.CurriculumId == curriculumId.Value);

            stages = await query
                .OrderBy(s => s.CurriculumId)
                .ThenBy(s => s.OrderIndex)
                .Select(s => new EducationStageDto(s.Id, s.CurriculumId, s.NameEn, s.NameAr, s.OrderIndex))
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

    public async Task<IEnumerable<CourseStateDto>> GetCourseStatesAsync(CancellationToken cancellationToken)
    {
        var cacheKey = "CourseStates";
        if (!_cache.TryGetValue(cacheKey, out IEnumerable<CourseStateDto>? states))
        {
            states = await _context.CourseStates
                .Where(s => s.IsActive)
                .OrderBy(s => s.OrderIndex)
                .Select(s => new CourseStateDto(s.Id, s.NameEn, s.NameAr, s.OrderIndex))
                .ToListAsync(cancellationToken);

            _cache.Set(cacheKey, states, TimeSpan.FromHours(24));
        }

        return states!;
    }
}
