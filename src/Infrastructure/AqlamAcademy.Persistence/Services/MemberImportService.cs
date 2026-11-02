using System.Security.Cryptography;
using System.Text.RegularExpressions;
using AqlamAcademy.Application.Common.Interfaces;
using AqlamAcademy.Application.Common.Models;
using AqlamAcademy.Application.Features.Organizations.Members;
using AqlamAcademy.Domain.Classes;
using AqlamAcademy.Domain.Organizations;
using AqlamAcademy.Domain.Staff;
using AqlamAcademy.Domain.Students;
using AqlamAcademy.Infrastructure.Persistence.Identity;
using ClosedXML.Excel;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AqlamAcademy.Infrastructure.Persistence.Services;

/// <summary>
/// Creates accounts and memberships from an Excel sheet (columns: FullName, Role, Phone, Email, Gender).
/// Each row is its own transaction: valid rows are created even when other rows fail.
/// </summary>
public class MemberImportService : IMemberImportService
{
    public const int MaxRows = 500;
    private const string PasswordAlphabet = "abcdefghjkmnpqrstuvwxyzABCDEFGHJKMNPQRSTUVWXYZ23456789";
    private static readonly string[] Columns = ["FullName", "Role", "Phone", "Email", "Gender"];

    /// <summary>Roles an import may grant, with the matching platform role (null = none).</summary>
    private static readonly Dictionary<string, (OrganizationRole Role, string? PlatformRole)> ImportableRoles =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["Student"] = (OrganizationRole.Student, "Student"),
            ["Parent"] = (OrganizationRole.Parent, null),
            ["Teacher"] = (OrganizationRole.Teacher, "Teacher"),
            ["Assistant"] = (OrganizationRole.Assistant, "Assistant"),
        };

    private readonly ApplicationDbContext _context;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IMembershipService _memberships;
    private readonly ILogger<MemberImportService> _logger;

    public MemberImportService(ApplicationDbContext context, UserManager<ApplicationUser> userManager,
        IMembershipService memberships, ILogger<MemberImportService> logger)
    {
        _context = context;
        _userManager = userManager;
        _memberships = memberships;
        _logger = logger;
    }

    public byte[] BuildTemplate()
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("Members");
        for (var i = 0; i < Columns.Length; i++)
            sheet.Cell(1, i + 1).Value = Columns[i];
        sheet.Row(1).Style.Font.Bold = true;
        sheet.Columns().Width = 28;

        var help = workbook.AddWorksheet("Instructions");
        help.Cell(1, 1).Value = "One member per row on the Members sheet. Do not change the header row.";
        help.Cell(2, 1).Value = "FullName: required. Role: Student (default), Parent, Teacher or Assistant. Phone and Email: optional. Gender: Male or Female (ذكر / أنثى), needed for boys' and girls' classes.";
        help.Cell(3, 1).Value = $"Up to {MaxRows} rows per file. Usernames and temporary passwords are generated and shown once after import.";

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    public async Task<Result<MemberImportReportDto>> ImportAsync(Guid organizationId, Stream workbookStream, CancellationToken cancellationToken)
    {
        var organization = await _context.Organizations.AsNoTracking().FirstOrDefaultAsync(o => o.Id == organizationId, cancellationToken);
        if (organization is null)
            return Result<MemberImportReportDto>.NotFound("Organization not found.");

        List<ImportRow> rows;
        try
        {
            rows = ReadRows(workbookStream);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Unreadable member import file for organization {OrganizationId}", organizationId);
            return Result<MemberImportReportDto>.Failure(new[] { "The file could not be read. Please upload the .xlsx template." });
        }

        if (rows.Count == 0)
            return Result<MemberImportReportDto>.Failure(new[] { "The file contains no members." });
        if (rows.Count > MaxRows)
            return Result<MemberImportReportDto>.Failure(new[] { $"A file can contain at most {MaxRows} members." });

        var existingMembers = await (
                from m in _context.Memberships
                join u in _context.Users on m.UserId equals u.Id
                where m.OrganizationId == organizationId && m.Status != MembershipStatus.Left
                select new { u.FullName, u.PhoneNumber })
            .ToListAsync(cancellationToken);
        var knownPeople = existingMembers.Select(m => PersonKey(m.FullName, m.PhoneNumber)).ToHashSet();

        var report = new List<MemberImportRowDto>();
        foreach (var row in rows)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var errors = Validate(row, knownPeople);
            if (errors.Count > 0)
            {
                report.Add(new MemberImportRowDto(row.RowNumber, row.FullName, row.Role, "Failed", null, null, errors));
                continue;
            }

            var created = await CreateMemberAsync(organization, row, cancellationToken);
            report.Add(created);
            if (created.Status == "Created")
                knownPeople.Add(PersonKey(row.FullName, row.Phone)); // catches duplicates inside the same file
        }

        var createdCount = report.Count(r => r.Status == "Created");
        _logger.LogInformation("Member import for organization {OrganizationId}: {Created} created, {Failed} failed",
            organizationId, createdCount, report.Count - createdCount);

        return Result<MemberImportReportDto>.Success(new MemberImportReportDto(createdCount, report.Count - createdCount, report));
    }

    private async Task<MemberImportRowDto> CreateMemberAsync(Organization organization, ImportRow row, CancellationToken cancellationToken)
    {
        var (role, platformRole) = ImportableRoles[row.Role];
        var userName = await GenerateUserNameAsync(organization.Slug);
        var password = GeneratePassword();

        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var user = new ApplicationUser
            {
                UserName = userName,
                FullName = row.FullName,
                PhoneNumber = row.Phone,
                Email = row.Email,
                EmailConfirmed = row.Email is not null,
                Gender = ParseGender(row.Gender),
                MustChangePassword = true
            };

            var result = await _userManager.CreateAsync(user, password);
            if (result.Succeeded && platformRole is not null)
                result = await _userManager.AddToRoleAsync(user, platformRole);
            if (!result.Succeeded)
            {
                await transaction.RollbackAsync(cancellationToken);
                _context.ChangeTracker.Clear();
                return new MemberImportRowDto(row.RowNumber, row.FullName, row.Role, "Failed", null, null,
                    result.Errors.Select(e => e.Description).ToList());
            }

            switch (role)
            {
                case OrganizationRole.Student: _context.StudentProfiles.Add(new StudentProfile { UserId = user.Id }); break;
                case OrganizationRole.Teacher: _context.TeacherProfiles.Add(new TeacherProfile { UserId = user.Id }); break;
                case OrganizationRole.Assistant: _context.AssistantProfiles.Add(new AssistantProfile { UserId = user.Id }); break;
            }

            _context.Memberships.Add(new Membership
            {
                OrganizationId = organization.Id,
                UserId = user.Id,
                Roles = role,
                Status = MembershipStatus.Active,
                Source = MembershipSource.BulkImport
            });

            await _context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            _memberships.Invalidate(user.Id, organization.Id);

            return new MemberImportRowDto(row.RowNumber, row.FullName, row.Role, "Created", userName, password, []);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await transaction.RollbackAsync(cancellationToken);
            _context.ChangeTracker.Clear();
            _logger.LogError(ex, "Failed to import row {Row} for organization {OrganizationId}", row.RowNumber, organization.Id);
            return new MemberImportRowDto(row.RowNumber, row.FullName, row.Role, "Failed", null, null, ["Unexpected error while creating this member."]);
        }
    }

    private static List<string> Validate(ImportRow row, HashSet<string> knownPeople)
    {
        var errors = new List<string>();
        if (row.FullName.Length < 3)
            errors.Add("FullName is required (at least 3 characters).");
        if (row.FullName.Length > 150)
            errors.Add("FullName is too long (maximum 150 characters).");
        if (!ImportableRoles.ContainsKey(row.Role))
            errors.Add($"Role '{row.Role}' is not valid. Use Student, Parent, Teacher or Assistant.");
        if (row.Phone is not null && !Regex.IsMatch(row.Phone, @"^\+?[0-9]{8,15}$"))
            errors.Add("Phone must contain 8 to 15 digits.");
        if (row.Email is not null && !Regex.IsMatch(row.Email, @"^[^@\s]+@[^@\s]+\.[^@\s]+$"))
            errors.Add("Email is not valid.");
        if (row.Gender is not null && ParseGender(row.Gender) is null)
            errors.Add("Gender must be Male or Female.");
        if (errors.Count == 0 && knownPeople.Contains(PersonKey(row.FullName, row.Phone)))
            errors.Add("Possible duplicate: a member with the same name and phone already exists.");
        return errors;
    }

    private static List<ImportRow> ReadRows(Stream stream)
    {
        using var workbook = new XLWorkbook(stream);
        var sheet = workbook.Worksheets.First();
        var header = sheet.Row(1);

        // Columns are found by header name, so their order in the file does not matter.
        var positions = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var cell in header.CellsUsed())
        {
            var name = Regex.Replace(cell.GetString(), @"\s+", string.Empty);
            if (Columns.Contains(name, StringComparer.OrdinalIgnoreCase))
                positions[name] = cell.Address.ColumnNumber;
        }
        if (!positions.ContainsKey("FullName"))
            throw new InvalidDataException("Missing FullName column.");

        string? Read(IXLRow row, string column) =>
            positions.TryGetValue(column, out var index) && row.Cell(index).GetString().Trim() is { Length: > 0 } value ? value : null;

        var rows = new List<ImportRow>();
        foreach (var row in sheet.RowsUsed().Where(r => r.RowNumber() > 1))
        {
            var fullName = Read(row, "FullName") ?? string.Empty;
            var role = Read(row, "Role") ?? "Student";
            var phone = Read(row, "Phone")?.Replace(" ", string.Empty).Replace("-", string.Empty);
            var email = Read(row, "Email");
            var gender = Read(row, "Gender");
            if (fullName.Length == 0 && phone is null && email is null)
                continue; // blank line
            rows.Add(new ImportRow(row.RowNumber(), fullName, role, phone, email, gender));
        }

        return rows;
    }

    private async Task<string> GenerateUserNameAsync(string organizationSlug)
    {
        var prefix = Regex.Replace(organizationSlug, "[^a-z0-9]", string.Empty);
        prefix = prefix.Length > 12 ? prefix[..12] : prefix;

        string userName;
        do userName = $"{prefix}.{RandomNumberGenerator.GetInt32(100000, 1000000)}";
        while (await _userManager.FindByNameAsync(userName) is not null);
        return userName;
    }

    private static string GeneratePassword()
    {
        // Always contains a digit; easy to read aloud or print (no 0/O, 1/l/I).
        var core = RandomNumberGenerator.GetString(PasswordAlphabet, 9);
        return core + RandomNumberGenerator.GetInt32(2, 10);
    }

    private static string PersonKey(string fullName, string? phone) =>
        $"{Regex.Replace(fullName.Trim().ToLowerInvariant(), @"\s+", " ")}|{phone}";

    private static Gender? ParseGender(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "male" or "m" or "ذكر" => Gender.Male,
        "female" or "f" or "أنثى" or "انثى" => Gender.Female,
        _ => null
    };

    private sealed record ImportRow(int RowNumber, string FullName, string Role, string? Phone, string? Email, string? Gender);
}
