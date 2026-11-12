using AqlamAcademy.Application.Common.Interfaces;
using AqlamAcademy.Application.Common.Models;
using AqlamAcademy.Application.Features.Payments;
using AqlamAcademy.Domain.Learning;
using AqlamAcademy.Domain.Organizations;
using AqlamAcademy.Domain.Payments;
using Microsoft.EntityFrameworkCore;

namespace AqlamAcademy.Infrastructure.Persistence.Payments;

/// <summary>Sales, commission and earnings of the current organization, plus the monthly fee statement for independent teachers.</summary>
public class EarningsService(ApplicationDbContext platform, ICurrentOrganization currentOrganization) : IEarningsService
{
    public async Task<Result<EarningsDto>> GetAsync(CancellationToken cancellationToken)
    {
        if (currentOrganization.OrganizationId is not { } orgId)
            return Result<EarningsDto>.NotFound("Organization not found.");
        var org = await platform.Organizations.AsNoTracking().FirstAsync(o => o.Id == orgId, cancellationToken);
        var fees = await platform.PlatformFees.AsNoTracking().FirstOrDefaultAsync(cancellationToken);

        var paid = platform.Payments.AsNoTracking().Where(p => p.OrganizationId == orgId && p.Status == PaymentStatus.Succeeded);
        var lines = await (from l in platform.LedgerEntries.AsNoTracking()
                           join p in paid on l.PaymentId equals p.Id
                           select new { l.Account, l.Amount, l.Currency, l.PaymentId }).ToListAsync(cancellationToken);

        var totals = lines.GroupBy(l => l.Currency).Select(g => new MoneyDto(
            g.Key,
            Gross: g.Where(l => l.Account is LedgerAccount.ProviderClearing or LedgerAccount.ProviderFees).Sum(l => l.Amount),
            Commission: -g.Where(l => l.Account == LedgerAccount.PlatformCommission).Sum(l => l.Amount),
            ProviderFees: g.Where(l => l.Account == LedgerAccount.ProviderFees).Sum(l => l.Amount),
            Net: -g.Where(l => l.Account == LedgerAccount.OrganizationEarnings).Sum(l => l.Amount))).ToList();

        var recent = await (from p in paid
                            join u in platform.Users.AsNoTracking() on p.UserId equals u.Id
                            orderby p.CompletedAtUtc descending
                            select new { p.Id, p.CompletedAtUtc, p.CourseTitle, u.FullName, u.UserName, p.Amount, p.Currency })
            .Take(20).ToListAsync(cancellationToken);
        var byPayment = lines.GroupBy(l => l.PaymentId!.Value).ToDictionary(g => g.Key, g => g.ToList());
        var sales = recent.Select(p =>
        {
            var l = byPayment.GetValueOrDefault(p.Id) ?? [];
            return new SaleDto(p.Id, p.CompletedAtUtc ?? DateTime.UtcNow, p.CourseTitle,
                string.IsNullOrWhiteSpace(p.FullName) ? p.UserName ?? string.Empty : p.FullName, p.Amount,
                -l.Where(x => x.Account == LedgerAccount.PlatformCommission).Sum(x => x.Amount),
                -l.Where(x => x.Account == LedgerAccount.OrganizationEarnings).Sum(x => x.Amount), p.Currency);
        }).ToList();

        var monthlyFee = org.Type == OrganizationType.Personal ? await MonthlyFeeAsync(org, fees, cancellationToken) : null;
        return Result<EarningsDto>.Success(new EarningsDto(org.Type.ToString(), org.CommissionPercent ?? fees?.CommissionPercent, totals, sales, monthlyFee, org.Currency));
    }

    /// <summary>
    /// Students the teacher registered themselves (code, join code, invitation, import…) who used a course this month.
    /// Anyone who bought a course here pays through commission and is left out, as are students who found a free
    /// course on the marketplace on their own.
    /// </summary>
    private async Task<MonthlyFeeDto> MonthlyFeeAsync(Organization org, PlatformFees? fees, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var monthStart = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);

        var buyers = platform.Enrollments.AsNoTracking()
            .Where(e => e.OrganizationId == org.Id && e.Source == EnrollmentSource.Purchase)
            .Select(e => e.UserId);
        var activeThisMonth = platform.Enrollments.AsNoTracking()
            .Where(e => e.OrganizationId == org.Id && e.LastAccessedAtUtc >= monthStart)
            .Select(e => e.UserId);

        var counted = await platform.Memberships.AsNoTracking()
            .Where(m => m.OrganizationId == org.Id && m.Status == MembershipStatus.Active
                        && (m.Roles & OrganizationRole.Student) == OrganizationRole.Student
                        && m.Source != MembershipSource.PublicEnrollment
                        && activeThisMonth.Contains(m.UserId) && !buyers.Contains(m.UserId))
            .Select(m => m.UserId).Distinct().CountAsync(cancellationToken);
        var excludedBuyers = await buyers.Where(b => activeThisMonth.Contains(b)).Distinct().CountAsync(cancellationToken);

        var rate = fees?.MonthlyFeeFor(org.Currency);
        return new MonthlyFeeDto(monthStart.ToString("yyyy-MM"), counted, excludedBuyers, rate, rate is { } r ? r * counted : null, org.Currency);
    }
}
