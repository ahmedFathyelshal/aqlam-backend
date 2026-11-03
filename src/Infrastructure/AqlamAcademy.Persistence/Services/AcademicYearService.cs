using AqlamAcademy.Application.Common.Models;
using AqlamAcademy.Application.Features.Academics;
using AqlamAcademy.Domain.Academics;
using Microsoft.EntityFrameworkCore;

namespace AqlamAcademy.Infrastructure.Persistence.Services;

/// <summary>Academic years and terms of the organization. The first year created becomes the current one.</summary>
public class AcademicYearService : IAcademicYearService
{
    private readonly TenantDbContext _tenant;

    public AcademicYearService(TenantDbContext tenant)
    {
        _tenant = tenant;
    }

    public async Task<IReadOnlyList<AcademicYearDto>> ListAsync(CancellationToken cancellationToken) =>
        (await _tenant.AcademicYears.AsNoTracking().Include(y => y.Terms).OrderByDescending(y => y.StartDate).ToListAsync(cancellationToken))
        .Select(ToDto).ToList();

    public async Task<Result<AcademicYearDto>> SaveAsync(Guid? id, string name, DateOnly startDate, DateOnly endDate, IReadOnlyList<AcademicTermInput> terms, CancellationToken cancellationToken)
    {
        var year = id is null ? null : await _tenant.AcademicYears.Include(y => y.Terms).FirstOrDefaultAsync(y => y.Id == id, cancellationToken);
        if (id is not null && year is null)
            return Result<AcademicYearDto>.NotFound("Academic year not found.");

        if (await _tenant.AcademicYears.AnyAsync(y => y.Id != id && y.Name.ToLower() == name.ToLower(), cancellationToken))
            return Result<AcademicYearDto>.Conflict("Another academic year already has this name.");

        if (year is null)
        {
            year = new AcademicYear { IsCurrent = !await _tenant.AcademicYears.AnyAsync(cancellationToken) };
            _tenant.AcademicYears.Add(year);

            // Classes created before the school set up its years belong to the first one.
            if (year.IsCurrent)
                foreach (var classGroup in await _tenant.ClassGroups.Where(c => c.AcademicYearId == null).ToListAsync(cancellationToken))
                    classGroup.AcademicYearId = year.Id;
        }

        // Terms holding grades stay; the rest follow the form.
        var keptIds = terms.Where(t => t.Id is not null).Select(t => t.Id!.Value).ToHashSet();
        var removed = year.Terms.Where(t => !keptIds.Contains(t.Id)).ToList();
        var removedIds = removed.Select(t => t.Id).ToList();
        if (removedIds.Count > 0 && await _tenant.GradeCategories.AnyAsync(c => removedIds.Contains(c.TermId), cancellationToken))
            return Result<AcademicYearDto>.Conflict("A term you removed already has grades.");
        if (keptIds.Except(year.Terms.Select(t => t.Id)).Any())
            return Result<AcademicYearDto>.Failure(["A term does not belong to this year."]);
        _tenant.AcademicTerms.RemoveRange(removed);

        year.Name = name;
        year.StartDate = startDate;
        year.EndDate = endDate;
        year.LastModifiedAtUtc = DateTime.UtcNow;

        var order = 1;
        foreach (var input in terms.OrderBy(t => t.StartDate))
        {
            var term = year.Terms.FirstOrDefault(t => t.Id == input.Id);
            if (term is null)
            {
                term = new AcademicTerm { AcademicYearId = year.Id };
                _tenant.AcademicTerms.Add(term); // added explicitly (preset key)
            }
            term.Name = input.Name.Trim();
            term.StartDate = input.StartDate;
            term.EndDate = input.EndDate;
            term.Order = order++;
        }

        await _tenant.SaveChangesAsync(cancellationToken);
        return Result<AcademicYearDto>.Success(ToDto(year));
    }

    public async Task<Result<bool>> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var year = await _tenant.AcademicYears.Include(y => y.Terms).FirstOrDefaultAsync(y => y.Id == id, cancellationToken);
        if (year is null)
            return Result<bool>.NotFound("Academic year not found.");
        if (await _tenant.ClassGroups.AnyAsync(c => c.AcademicYearId == id, cancellationToken))
            return Result<bool>.Conflict("This year still has classes.");
        var termIds = year.Terms.Select(t => t.Id).ToList();
        if (await _tenant.GradeCategories.AnyAsync(c => termIds.Contains(c.TermId), cancellationToken))
            return Result<bool>.Conflict("This year already has grades.");

        _tenant.AcademicTerms.RemoveRange(year.Terms);
        _tenant.AcademicYears.Remove(year);
        await _tenant.SaveChangesAsync(cancellationToken);
        return Result<bool>.Success(true);
    }

    public async Task<Result<AcademicYearDto>> SetCurrentAsync(Guid id, CancellationToken cancellationToken)
    {
        var years = await _tenant.AcademicYears.Include(y => y.Terms).ToListAsync(cancellationToken);
        var year = years.FirstOrDefault(y => y.Id == id);
        if (year is null)
            return Result<AcademicYearDto>.NotFound("Academic year not found.");

        foreach (var other in years)
            other.IsCurrent = other.Id == id;
        await _tenant.SaveChangesAsync(cancellationToken);
        return Result<AcademicYearDto>.Success(ToDto(year));
    }

    public async Task<Result<AcademicTermDto>> SetReportCardsPublishedAsync(Guid termId, bool published, CancellationToken cancellationToken)
    {
        var term = await _tenant.AcademicTerms.FirstOrDefaultAsync(t => t.Id == termId, cancellationToken);
        if (term is null)
            return Result<AcademicTermDto>.NotFound("Term not found.");

        term.ReportCardsPublished = published;
        await _tenant.SaveChangesAsync(cancellationToken);
        return Result<AcademicTermDto>.Success(ToDto(term));
    }

    private static AcademicYearDto ToDto(AcademicYear y) =>
        new(y.Id, y.Name, y.StartDate, y.EndDate, y.IsCurrent, y.Terms.OrderBy(t => t.Order).Select(ToDto).ToList());

    private static AcademicTermDto ToDto(AcademicTerm t) => new(t.Id, t.Name, t.Order, t.StartDate, t.EndDate, t.ReportCardsPublished);
}
