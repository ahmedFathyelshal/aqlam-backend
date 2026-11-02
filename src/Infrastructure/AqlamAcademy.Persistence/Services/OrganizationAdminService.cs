using AqlamAcademy.Application.Common.Interfaces;
using AqlamAcademy.Application.Common.Models;
using AqlamAcademy.Application.Features.Platform.Organizations;
using AqlamAcademy.Domain.Organizations;
using AqlamAcademy.Infrastructure.Persistence.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using AqlamAcademy.Infrastructure.Persistence.MultiTenancy;

namespace AqlamAcademy.Infrastructure.Persistence.Services;

public class OrganizationAdminService : IOrganizationAdminService
{
    private readonly ApplicationDbContext _context;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly CellSettings _cell;
    private readonly ConnectionStringProtector _protector;
    private readonly TenantDatabaseMigrator _tenantDatabases;

    public OrganizationAdminService(ApplicationDbContext context, UserManager<ApplicationUser> userManager, IOptions<CellSettings> cellSettings,
        ConnectionStringProtector protector, TenantDatabaseMigrator tenantDatabases)
    {
        _protector = protector;
        _tenantDatabases = tenantDatabases;
        _context = context;
        _userManager = userManager;
        _cell = cellSettings.Value;
    }

    public async Task<Result<OrganizationDto>> CreateAsync(
        string name,
        string slug,
        OrganizationType type,
        string countryCode,
        string ownerUserName,
        IsolationTier isolationTier,
        string? dedicatedConnectionString,
        CancellationToken cancellationToken)
    {
        if (await _context.Organizations.IgnoreQueryFilters().AnyAsync(o => o.Slug == slug, cancellationToken))
            return Result<OrganizationDto>.Conflict("This slug is already taken.");

        var owner = await _userManager.FindByNameAsync(ownerUserName);
        if (owner is null)
            return Result<OrganizationDto>.Failure(new[] { $"User '{ownerUserName}' was not found." });

        var (currency, timeZone) = LocaleDefaults(countryCode);

        var organization = new Organization
        {
            Name = name,
            Slug = slug,
            Type = type,
            CountryCode = countryCode,
            Currency = currency,
            TimeZone = timeZone,
            // Schools are private by default; centers sell to the public like independent teachers.
            PublicPublishing = type == OrganizationType.School ? PublicPublishingPolicy.Disabled : PublicPublishingPolicy.Allowed,
            IsolationTier = isolationTier
        };

        if (isolationTier == IsolationTier.Dedicated)
        {
            // The database must exist and be reachable: create the tenant schema now, before the organization
            // is saved, so a school is never left pointing at a database that does not work.
            try
            {
                await _tenantDatabases.MigrateAsync(organization.Id, slug, dedicatedConnectionString!, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return Result<OrganizationDto>.Failure(new[] { $"The dedicated database could not be prepared: {ex.GetBaseException().Message}" });
            }

            organization.DedicatedConnectionString = _protector.Protect(dedicatedConnectionString!);
        }

        organization.Domains.Add(new OrganizationDomain
        {
            Host = $"{slug}.{_cell.BaseDomain}".ToLowerInvariant(),
            IsPrimary = true
        });

        organization.Memberships.Add(new Membership
        {
            UserId = owner.Id,
            Roles = OrganizationRole.Owner | OrganizationRole.Admin,
            Source = MembershipSource.Owner
        });

        _context.Organizations.Add(organization);

        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Most likely lost a race with another request creating the same slug (unique index); otherwise rethrow.
            _context.ChangeTracker.Clear();
            if (await _context.Organizations.IgnoreQueryFilters().AnyAsync(o => o.Slug == slug, cancellationToken))
                return Result<OrganizationDto>.Conflict("This slug is already taken.");
            throw;
        }

        return Result<OrganizationDto>.Success(ToDto(organization));
    }

    public async Task<IReadOnlyList<OrganizationDto>> ListAsync(OrganizationType? type, CancellationToken cancellationToken)
    {
        var query = _context.Organizations.AsNoTracking().Include(o => o.Domains).AsQueryable();
        if (type.HasValue)
            query = query.Where(o => o.Type == type.Value);

        var organizations = await query.OrderBy(o => o.Name).ToListAsync(cancellationToken);
        return organizations.Select(ToDto).ToList();
    }

    private static (string Currency, string TimeZone) LocaleDefaults(string countryCode) => countryCode switch
    {
        "EG" => ("EGP", "Africa/Cairo"),
        _ => ("SAR", "Asia/Riyadh")
    };

    private static OrganizationDto ToDto(Organization o) => new(
        o.Id,
        o.Name,
        o.Slug,
        o.Type.ToString(),
        o.Status.ToString(),
        o.CountryCode,
        o.Currency,
        o.TimeZone,
        o.IsolationTier.ToString(),
        o.PublicPublishing.ToString(),
        o.Domains.FirstOrDefault(d => d.IsPrimary)?.Host);
}
