using System.Text.RegularExpressions;
using AqlamAcademy.Domain.Organizations;
using AqlamAcademy.Infrastructure.Persistence.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AqlamAcademy.Infrastructure.Persistence.Services;

/// <summary>
/// Every teacher owns a personal organization (their academy). It is created with the account so the
/// teacher can start building courses immediately; the word "organization" is never shown to them.
/// Adds to the context only; the caller saves (inside its own transaction).
/// </summary>
public class PersonalOrganizationFactory
{
    private readonly ApplicationDbContext _context;
    private readonly CellSettings _cell;

    public PersonalOrganizationFactory(ApplicationDbContext context, IOptions<CellSettings> cellSettings)
    {
        _context = context;
        _cell = cellSettings.Value;
    }

    public async Task<Organization> AddForAsync(ApplicationUser user)
    {
        var organization = new Organization
        {
            Name = string.IsNullOrWhiteSpace(user.FullName) ? user.UserName! : user.FullName,
            Slug = await GenerateUniqueSlugAsync(user.UserName!),
            Type = OrganizationType.Personal,
            CountryCode = _cell.DefaultCountryCode,
            Currency = _cell.DefaultCurrency,
            TimeZone = _cell.DefaultTimeZone,
            PublicPublishing = PublicPublishingPolicy.Allowed
        };

        organization.Memberships.Add(new Membership
        {
            UserId = user.Id,
            Roles = OrganizationRole.Owner | OrganizationRole.Teacher,
            Source = MembershipSource.Owner
        });

        _context.Organizations.Add(organization);
        return organization;
    }

    private async Task<string> GenerateUniqueSlugAsync(string source)
    {
        var baseSlug = Regex.Replace(source.ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');
        if (baseSlug.Length < 3)
            baseSlug = $"academy-{baseSlug}".Trim('-');
        if (baseSlug.Length > 50)
            baseSlug = baseSlug[..50].Trim('-');

        var slug = baseSlug;
        var suffix = 2;
        // Check both saved rows and organizations added earlier in this unit of work (e.g. during backfill).
        while (_context.Organizations.Local.Any(o => o.Slug == slug) ||
               await _context.Organizations.IgnoreQueryFilters().AnyAsync(o => o.Slug == slug))
        {
            slug = $"{baseSlug}-{suffix++}";
        }

        return slug;
    }
}
