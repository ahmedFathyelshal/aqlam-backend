using AqlamAcademy.Application.Common.Authorization;
using AqlamAcademy.Application.Common.Interfaces;
using AqlamAcademy.Application.Common.Models;
using AqlamAcademy.Application.Features.Academics;
using AqlamAcademy.Domain.Academics;
using AqlamAcademy.Domain.Assessments;
using AqlamAcademy.Domain.Classes;
using AqlamAcademy.Domain.Enums;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;

namespace AqlamAcademy.Infrastructure.Persistence.Services;

/// <summary>
/// Reads grades the same way for the gradebook and report cards. A typed score wins; otherwise a linked item
/// takes the student's best submitted quiz attempt or graded homework, scaled to the item's points.
/// </summary>
public class GradeReader
{
    private readonly TenantDbContext _tenant;

    public GradeReader(TenantDbContext tenant)
    {
        _tenant = tenant;
    }

    public Task<List<GradeCategory>> CategoriesAsync(Guid assignmentId, Guid termId, CancellationToken cancellationToken) =>
        _tenant.GradeCategories.AsNoTracking()
            .Include(c => c.Items).ThenInclude(i => i.Entries)
            .AsSplitQuery()
            .Where(c => c.TeachingAssignmentId == assignmentId && c.TermId == termId)
            .OrderBy(c => c.Order)
            .ToListAsync(cancellationToken);

    /// <summary>Fraction (0–1) earned per lesson and student, for the lessons linked to the given categories.</summary>
    public async Task<Dictionary<(Guid LessonId, Guid UserId), decimal>> LessonResultsAsync(IEnumerable<GradeCategory> categories, IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken)
    {
        var lessonIds = categories.SelectMany(c => c.Items).Where(i => i.SourceLessonId is not null).Select(i => i.SourceLessonId!.Value).Distinct().ToList();
        var results = new Dictionary<(Guid, Guid), decimal>();
        if (lessonIds.Count == 0 || userIds.Count == 0)
            return results;

        var attempts = await _tenant.QuizAttempts.AsNoTracking()
            .Where(a => lessonIds.Contains(a.LessonId) && userIds.Contains(a.UserId) && a.SubmittedAtUtc != null)
            .GroupBy(a => new { a.LessonId, a.UserId })
            .Select(g => new { g.Key.LessonId, g.Key.UserId, Best = g.Max(a => a.Percent) })
            .ToListAsync(cancellationToken);
        foreach (var a in attempts)
            results[(a.LessonId, a.UserId)] = a.Best / 100;

        var maxPoints = await _tenant.Assignments.AsNoTracking().Where(a => lessonIds.Contains(a.LessonId)).ToDictionaryAsync(a => a.LessonId, a => a.MaxPoints, cancellationToken);
        var submissions = await _tenant.AssignmentSubmissions.AsNoTracking()
            .Where(s => lessonIds.Contains(s.LessonId) && userIds.Contains(s.UserId) && s.Status == SubmissionStatus.Graded && s.Score != null)
            .Select(s => new { s.LessonId, s.UserId, s.Score })
            .ToListAsync(cancellationToken);
        foreach (var s in submissions)
            results[(s.LessonId, s.UserId)] = s.Score!.Value / maxPoints.GetValueOrDefault(s.LessonId, 10);

        return results;
    }

    public static (decimal? Score, bool FromLesson) ScoreOf(GradeItem item, Guid userId, IReadOnlyDictionary<(Guid LessonId, Guid UserId), decimal> lessonResults)
    {
        var typed = item.Entries.FirstOrDefault(e => e.UserId == userId);
        if (typed is not null)
            return (typed.Score, false);
        if (item.SourceLessonId is { } lessonId && lessonResults.TryGetValue((lessonId, userId), out var fraction))
            return (Math.Round(fraction * item.MaxPoints, 2, MidpointRounding.AwayFromZero), true);
        return (null, false);
    }

    /// <summary>Category percents and the weighted final for one student.</summary>
    public static (Dictionary<Guid, decimal?> Categories, decimal? Final) Compute(IReadOnlyList<GradeCategory> categories, Guid userId, IReadOnlyDictionary<(Guid LessonId, Guid UserId), decimal> lessonResults)
    {
        var percents = categories.ToDictionary(
            c => c.Id,
            c => GradeCalculator.CategoryPercent(c.Items.Select(i => (ScoreOf(i, userId, lessonResults).Score, i.MaxPoints))));
        return (percents, GradeCalculator.FinalPercent(categories.Select(c => (c.WeightPercent, percents[c.Id]))));
    }
}

/// <summary>Gradebook of one subject in one class and term. Teachers reach their own subjects; class managers reach all.</summary>
public class GradebookService : IGradebookService
{
    private readonly TenantDbContext _tenant;
    private readonly ApplicationDbContext _platform;
    private readonly ICurrentUserService _currentUser;
    private readonly ICurrentOrganization _currentOrganization;
    private readonly IMembershipService _memberships;
    private readonly GradeReader _reader;

    public GradebookService(TenantDbContext tenant, ApplicationDbContext platform, ICurrentUserService currentUser,
        ICurrentOrganization currentOrganization, IMembershipService memberships, GradeReader reader)
    {
        _tenant = tenant;
        _platform = platform;
        _currentUser = currentUser;
        _currentOrganization = currentOrganization;
        _memberships = memberships;
        _reader = reader;
    }

    public async Task<Result<GradebookDto>> GetAsync(Guid classId, Guid assignmentId, Guid termId, CancellationToken cancellationToken)
    {
        var access = await ResolveAsync(classId, assignmentId, cancellationToken);
        if (access.Failure is { } failure)
            return Result<GradebookDto>.Failure(failure.ErrorType, failure.Errors);

        var term = await _tenant.AcademicTerms.AsNoTracking().FirstOrDefaultAsync(t => t.Id == termId, cancellationToken);
        if (term is null)
            return Result<GradebookDto>.NotFound("Term not found.");

        var categories = await _reader.CategoriesAsync(assignmentId, termId, cancellationToken);
        var roster = await RosterAsync(classId, cancellationToken);
        var results = await _reader.LessonResultsAsync(categories, roster.Select(s => s.Id).ToList(), cancellationToken);
        var scale = await ScaleAsync(cancellationToken);

        var rows = roster.Select(s =>
        {
            var (percents, final) = GradeReader.Compute(categories, s.Id, results);
            var cells = categories.SelectMany(c => c.Items).Select(i => (Item: i, Value: GradeReader.ScoreOf(i, s.Id, results)))
                .Where(x => x.Value.Score is not null)
                .Select(x => new GradeCellDto(x.Item.Id, x.Value.Score!.Value, x.Value.FromLesson))
                .ToList();
            return new GradeRowDto(s.Id, s.UserName, s.FullName, cells, percents, final, GradeCalculator.Label(final, scale));
        }).ToList();

        return Result<GradebookDto>.Success(new GradebookDto(
            classId, access.ClassName!, assignmentId, access.Assignment!.SubjectName, termId, term.Name, scale.ToString(),
            categories.Sum(c => c.WeightPercent),
            categories.Select(ToDto).ToList(),
            rows,
            await LinkableLessonsAsync(access.Assignment.CourseId, cancellationToken)));
    }

    public async Task<Result<GradeCategoryDto>> SaveCategoryAsync(Guid classId, Guid assignmentId, Guid? categoryId, Guid termId, string name, decimal weightPercent, CancellationToken cancellationToken)
    {
        var access = await ResolveAsync(classId, assignmentId, cancellationToken);
        if (access.Failure is { } failure)
            return Result<GradeCategoryDto>.Failure(failure.ErrorType, failure.Errors);

        GradeCategory? category;
        if (categoryId is null)
        {
            if (!await _tenant.AcademicTerms.AnyAsync(t => t.Id == termId, cancellationToken))
                return Result<GradeCategoryDto>.NotFound("Term not found.");
            var count = await _tenant.GradeCategories.CountAsync(c => c.TeachingAssignmentId == assignmentId && c.TermId == termId, cancellationToken);
            category = new GradeCategory { TeachingAssignmentId = assignmentId, TermId = termId, Order = count };
            _tenant.GradeCategories.Add(category);
        }
        else
        {
            category = await _tenant.GradeCategories.Include(c => c.Items).FirstOrDefaultAsync(c => c.Id == categoryId && c.TeachingAssignmentId == assignmentId, cancellationToken);
            if (category is null)
                return Result<GradeCategoryDto>.NotFound("Category not found.");
        }

        category.Name = name;
        category.WeightPercent = weightPercent;
        await _tenant.SaveChangesAsync(cancellationToken);
        return Result<GradeCategoryDto>.Success(ToDto(category));
    }

    public async Task<Result<bool>> DeleteCategoryAsync(Guid classId, Guid assignmentId, Guid categoryId, CancellationToken cancellationToken)
    {
        var access = await ResolveAsync(classId, assignmentId, cancellationToken);
        if (access.Failure is { } failure)
            return Result<bool>.Failure(failure.ErrorType, failure.Errors);

        var category = await _tenant.GradeCategories.Include(c => c.Items).FirstOrDefaultAsync(c => c.Id == categoryId && c.TeachingAssignmentId == assignmentId, cancellationToken);
        if (category is null)
            return Result<bool>.NotFound("Category not found.");
        if (category.Items.Count > 0)
            return Result<bool>.Conflict("Delete the items of this category first.");

        _tenant.GradeCategories.Remove(category);
        await _tenant.SaveChangesAsync(cancellationToken);
        return Result<bool>.Success(true);
    }

    public async Task<Result<GradeItemDto>> SaveItemAsync(Guid classId, Guid assignmentId, Guid categoryId, Guid? itemId, string title, decimal maxPoints, Guid? sourceLessonId, CancellationToken cancellationToken)
    {
        var access = await ResolveAsync(classId, assignmentId, cancellationToken);
        if (access.Failure is { } failure)
            return Result<GradeItemDto>.Failure(failure.ErrorType, failure.Errors);

        if (!await _tenant.GradeCategories.AnyAsync(c => c.Id == categoryId && c.TeachingAssignmentId == assignmentId, cancellationToken))
            return Result<GradeItemDto>.NotFound("Category not found.");

        var source = GradeSource.Manual;
        if (sourceLessonId is { } lessonId)
        {
            // Only quiz and homework lessons of this subject's own course can feed the gradebook.
            var lessonType = await _tenant.CourseLessons.AsNoTracking()
                .Where(l => l.Id == lessonId && l.Chapter.CourseId == access.Assignment!.CourseId)
                .Select(l => (LessonType?)l.Type)
                .FirstOrDefaultAsync(cancellationToken);
            source = lessonType switch
            {
                LessonType.Quiz => GradeSource.Quiz,
                LessonType.Assignment => GradeSource.Assignment,
                _ => GradeSource.Manual
            };
            if (source == GradeSource.Manual)
                return Result<GradeItemDto>.Failure(["Choose a quiz or homework lesson of this subject's course."]);
        }

        GradeItem? item;
        if (itemId is null)
        {
            item = new GradeItem { CategoryId = categoryId };
            _tenant.GradeItems.Add(item);
        }
        else
        {
            item = await _tenant.GradeItems.FirstOrDefaultAsync(i => i.Id == itemId && i.CategoryId == categoryId, cancellationToken);
            if (item is null)
                return Result<GradeItemDto>.NotFound("Item not found.");
        }

        item.Title = title;
        item.MaxPoints = maxPoints;
        item.Source = source;
        item.SourceLessonId = sourceLessonId;
        await _tenant.SaveChangesAsync(cancellationToken);
        return Result<GradeItemDto>.Success(ToDto(item));
    }

    public async Task<Result<bool>> DeleteItemAsync(Guid classId, Guid assignmentId, Guid itemId, CancellationToken cancellationToken)
    {
        var access = await ResolveAsync(classId, assignmentId, cancellationToken);
        if (access.Failure is { } failure)
            return Result<bool>.Failure(failure.ErrorType, failure.Errors);

        var item = await FindItemAsync(assignmentId, itemId, cancellationToken);
        if (item is null)
            return Result<bool>.NotFound("Item not found.");

        _tenant.GradeItems.Remove(item);
        await _tenant.SaveChangesAsync(cancellationToken);
        return Result<bool>.Success(true);
    }

    public async Task<Result<bool>> SaveScoresAsync(Guid classId, Guid assignmentId, Guid itemId, IReadOnlyList<GradeScoreInput> scores, CancellationToken cancellationToken)
    {
        var access = await ResolveAsync(classId, assignmentId, cancellationToken);
        if (access.Failure is { } failure)
            return Result<bool>.Failure(failure.ErrorType, failure.Errors);

        var item = await FindItemAsync(assignmentId, itemId, cancellationToken);
        if (item is null)
            return Result<bool>.NotFound("Item not found.");

        if (scores.Any(s => s.Score > item.MaxPoints))
            return Result<bool>.Failure([$"A score cannot be higher than {item.MaxPoints}."]);

        var roster = await _tenant.ClassStudents.Where(s => s.ClassGroupId == classId).Select(s => s.UserId).ToListAsync(cancellationToken);
        if (scores.Any(s => !roster.Contains(s.UserId)))
            return Result<bool>.Failure(["Some students are not in this class."]);

        var entries = await _tenant.GradeEntries.Where(e => e.ItemId == itemId).ToListAsync(cancellationToken);
        foreach (var input in scores)
        {
            var entry = entries.FirstOrDefault(e => e.UserId == input.UserId);
            if (input.Score is not { } score)
            {
                if (entry is not null) // cleared: a linked item goes back to the lesson's score
                    _tenant.GradeEntries.Remove(entry);
            }
            else if (entry is null)
            {
                _tenant.GradeEntries.Add(new GradeEntry { ItemId = itemId, UserId = input.UserId, Score = score });
            }
            else
            {
                entry.Score = score;
            }
        }

        await _tenant.SaveChangesAsync(cancellationToken);
        return Result<bool>.Success(true);
    }

    public async Task<Result<GradebookFile>> ExportAsync(Guid classId, Guid assignmentId, Guid termId, CancellationToken cancellationToken)
    {
        var book = await GetAsync(classId, assignmentId, termId, cancellationToken);
        if (!book.Succeeded)
            return Result<GradebookFile>.Failure(book.ErrorType, book.Errors);

        var data = book.Data!;
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Grades");
        sheet.RightToLeft = true;

        var items = data.Categories.SelectMany(c => c.Items).ToList();
        var headers = new List<string> { "اسم المستخدم", "الطالب" };
        headers.AddRange(items.Select(i => $"{i.Title} ({i.MaxPoints:0.##})"));
        headers.AddRange(data.Categories.Select(c => $"{c.Name} % ({c.WeightPercent:0.##})"));
        headers.Add("المجموع %");
        headers.Add("التقدير");
        for (var i = 0; i < headers.Count; i++)
            sheet.Cell(1, i + 1).Value = headers[i];
        sheet.Row(1).Style.Font.Bold = true;

        var rowNumber = 2;
        foreach (var student in data.Students)
        {
            var column = 1;
            sheet.Cell(rowNumber, column++).Value = student.UserName;
            sheet.Cell(rowNumber, column++).Value = student.FullName;
            foreach (var item in items)
                SetNumber(sheet.Cell(rowNumber, column++), student.Cells.FirstOrDefault(c => c.ItemId == item.Id)?.Score);
            foreach (var category in data.Categories)
                SetNumber(sheet.Cell(rowNumber, column++), student.CategoryPercents.GetValueOrDefault(category.Id));
            SetNumber(sheet.Cell(rowNumber, column++), student.FinalPercent);
            sheet.Cell(rowNumber, column).Value = LabelText(student.Label);
            rowNumber++;
        }
        sheet.Columns().AdjustToContents();

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return Result<GradebookFile>.Success(new GradebookFile(stream.ToArray(), $"grades-{data.ClassName}-{data.SubjectName}-{data.TermName}.xlsx"));
    }

    private static void SetNumber(IXLCell cell, decimal? value)
    {
        if (value is { } number)
            cell.Value = (double)number;
    }

    private static string LabelText(string? label) => label switch
    {
        "Excellent" => "ممتاز",
        "VeryGood" => "جيد جدًا",
        "Good" => "جيد",
        "Pass" => "مقبول",
        "Fail" => "ضعيف",
        _ => label ?? string.Empty
    };

    private sealed record Access(TeachingAssignment? Assignment, string? ClassName, Result? Failure);

    private async Task<Access> ResolveAsync(Guid classId, Guid assignmentId, CancellationToken cancellationToken)
    {
        var assignment = await _tenant.TeachingAssignments.AsNoTracking().FirstOrDefaultAsync(a => a.Id == assignmentId && a.ClassGroupId == classId, cancellationToken);
        var className = await _tenant.ClassGroups.AsNoTracking().Where(c => c.Id == classId).Select(c => c.Name).FirstOrDefaultAsync(cancellationToken);
        if (assignment is null || className is null)
            return new(null, null, Result<bool>.NotFound("Subject not found in this class."));

        var roles = await _memberships.GetActiveRolesAsync(_currentUser.UserId, _currentOrganization.OrganizationId!.Value, cancellationToken);
        if (assignment.TeacherUserId != _currentUser.UserId && !Permissions.Grants(roles, Permissions.ClassesManage))
            return new(null, null, Result<bool>.Failure(ResultErrorType.Forbidden, "You can only open the gradebook of subjects you teach."));

        return new(assignment, className, null);
    }

    private Task<GradeItem?> FindItemAsync(Guid assignmentId, Guid itemId, CancellationToken cancellationToken) =>
        (from i in _tenant.GradeItems
         join c in _tenant.GradeCategories on i.CategoryId equals c.Id
         where i.Id == itemId && c.TeachingAssignmentId == assignmentId
         select i).FirstOrDefaultAsync(cancellationToken);

    private async Task<List<(Guid Id, string UserName, string FullName)>> RosterAsync(Guid classId, CancellationToken cancellationToken)
    {
        var ids = await _tenant.ClassStudents.AsNoTracking().Where(s => s.ClassGroupId == classId).Select(s => s.UserId).ToListAsync(cancellationToken);
        var users = await _platform.Users.AsNoTracking().Where(u => ids.Contains(u.Id)).ToListAsync(cancellationToken);
        return users.Select(u => (u.Id, u.UserName!, u.DisplayName)).OrderBy(u => u.DisplayName).ToList();
    }

    private async Task<GradingScale> ScaleAsync(CancellationToken cancellationToken) =>
        await _platform.Organizations.AsNoTracking().Where(o => o.Id == _currentOrganization.OrganizationId).Select(o => o.GradingScale).FirstAsync(cancellationToken);

    private async Task<IReadOnlyList<LinkableLessonDto>> LinkableLessonsAsync(Guid courseId, CancellationToken cancellationToken) =>
        (await _tenant.CourseLessons.AsNoTracking()
            .Where(l => l.Chapter.CourseId == courseId && (l.Type == LessonType.Quiz || l.Type == LessonType.Assignment))
            .OrderBy(l => l.Chapter.Order).ThenBy(l => l.Order)
            .Select(l => new { l.Id, l.Title, l.Type })
            .ToListAsync(cancellationToken))
        .Select(l => new LinkableLessonDto(l.Id, l.Title, l.Type.ToString())).ToList();

    private static GradeCategoryDto ToDto(GradeCategory c) =>
        new(c.Id, c.Name, c.WeightPercent, c.Items.OrderBy(i => i.CreatedAtUtc).Select(ToDto).ToList());

    private static GradeItemDto ToDto(GradeItem i) => new(i.Id, i.Title, i.MaxPoints, i.Source.ToString(), i.SourceLessonId);
}

/// <summary>Report card of one student in one term, across every class and subject they belong to.</summary>
public class ReportCardService : IReportCardService
{
    private readonly TenantDbContext _tenant;
    private readonly ApplicationDbContext _platform;
    private readonly ICurrentUserService _currentUser;
    private readonly ICurrentOrganization _currentOrganization;
    private readonly GradeReader _reader;

    public ReportCardService(TenantDbContext tenant, ApplicationDbContext platform, ICurrentUserService currentUser, ICurrentOrganization currentOrganization, GradeReader reader)
    {
        _tenant = tenant;
        _platform = platform;
        _currentUser = currentUser;
        _currentOrganization = currentOrganization;
        _reader = reader;
    }

    public Task<Result<ReportCardDto>> ForStaffAsync(Guid studentId, Guid termId, CancellationToken cancellationToken) =>
        BuildAsync(studentId, termId, publishedOnly: false, cancellationToken);

    public async Task<Result<ReportCardDto>> ForFamilyAsync(Guid studentId, Guid termId, CancellationToken cancellationToken) =>
        await IsFamilyAsync(studentId, cancellationToken)
            ? await BuildAsync(studentId, termId, publishedOnly: true, cancellationToken)
            : Result<ReportCardDto>.NotFound("Report card not found.");

    public async Task<Result<IReadOnlyList<PublishedTermDto>>> PublishedTermsAsync(Guid studentId, CancellationToken cancellationToken)
    {
        if (!await IsFamilyAsync(studentId, cancellationToken))
            return Result<IReadOnlyList<PublishedTermDto>>.NotFound("Student not found.");

        var terms = await (
                from t in _tenant.AcademicTerms.AsNoTracking()
                join y in _tenant.AcademicYears.AsNoTracking() on t.AcademicYearId equals y.Id
                where t.ReportCardsPublished
                orderby t.StartDate descending
                select new PublishedTermDto(t.Id, t.Name, y.Name))
            .ToListAsync(cancellationToken);
        return Result<IReadOnlyList<PublishedTermDto>>.Success(terms);
    }

    /// <summary>The student themself, or a parent linked to them, and the student belongs to this organization.</summary>
    private async Task<bool> IsFamilyAsync(Guid studentId, CancellationToken cancellationToken)
    {
        var me = _currentUser.UserId;
        var related = me == studentId || await _platform.ParentLinks.AnyAsync(l => l.ParentUserId == me && l.ChildUserId == studentId, cancellationToken);
        return related && await _platform.Memberships.AnyAsync(m => m.UserId == studentId && m.OrganizationId == _currentOrganization.OrganizationId
            && m.Status == Domain.Organizations.MembershipStatus.Active && (m.Roles & Domain.Organizations.OrganizationRole.Student) != 0, cancellationToken);
    }

    private async Task<Result<ReportCardDto>> BuildAsync(Guid studentId, Guid termId, bool publishedOnly, CancellationToken cancellationToken)
    {
        var term = await _tenant.AcademicTerms.AsNoTracking().FirstOrDefaultAsync(t => t.Id == termId, cancellationToken);
        if (term is null || (publishedOnly && !term.ReportCardsPublished))
            return Result<ReportCardDto>.NotFound("Report card not found.");
        var year = await _tenant.AcademicYears.AsNoTracking().FirstAsync(y => y.Id == term.AcademicYearId, cancellationToken);

        var student = await _platform.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == studentId, cancellationToken);
        var org = await _platform.Organizations.AsNoTracking().FirstOrDefaultAsync(o => o.Id == _currentOrganization.OrganizationId, cancellationToken);
        var isMember = await _platform.Memberships.AnyAsync(m => m.UserId == studentId && m.OrganizationId == _currentOrganization.OrganizationId, cancellationToken);
        if (student is null || org is null || !isMember)
            return Result<ReportCardDto>.NotFound("Student not found.");

        var classes = await (
                from s in _tenant.ClassStudents.AsNoTracking()
                join c in _tenant.ClassGroups.AsNoTracking() on s.ClassGroupId equals c.Id
                where s.UserId == studentId && (c.AcademicYearId == null || c.AcademicYearId == year.Id)
                orderby c.Name
                select new { c.Id, c.Name })
            .ToListAsync(cancellationToken);
        var classIds = classes.Select(c => c.Id).ToList();
        var assignments = await _tenant.TeachingAssignments.AsNoTracking().Where(a => classIds.Contains(a.ClassGroupId)).OrderBy(a => a.SubjectName).ToListAsync(cancellationToken);
        var teacherIds = assignments.Select(a => a.TeacherUserId).Distinct().ToList();
        var teachers = (await _platform.Users.AsNoTracking().Where(u => teacherIds.Contains(u.Id)).ToListAsync(cancellationToken)).ToDictionary(u => u.Id, u => u.DisplayName);

        var subjects = new List<ReportSubjectDto>();
        foreach (var assignment in assignments)
        {
            var categories = await _reader.CategoriesAsync(assignment.Id, termId, cancellationToken);
            if (categories.Count == 0)
                continue; // nothing graded for this subject in this term
            var results = await _reader.LessonResultsAsync(categories, [studentId], cancellationToken);
            var (percents, final) = GradeReader.Compute(categories, studentId, results);
            subjects.Add(new ReportSubjectDto(
                assignment.SubjectName,
                teachers.GetValueOrDefault(assignment.TeacherUserId),
                categories.Select(c => new ReportCategoryDto(c.Name, c.WeightPercent, percents[c.Id])).ToList(),
                final,
                GradeCalculator.Label(final, org.GradingScale)));
        }

        var finals = subjects.Where(s => s.FinalPercent is not null).Select(s => s.FinalPercent!.Value).ToList();
        decimal? overall = finals.Count == 0 ? null : Math.Round(finals.Average(), 1, MidpointRounding.AwayFromZero);

        var statuses = await (
                from r in _tenant.AttendanceRecords.AsNoTracking()
                join s in _tenant.AttendanceSessions.AsNoTracking() on r.SessionId equals s.Id
                where r.UserId == studentId && s.Date >= term.StartDate && s.Date <= term.EndDate
                select r.Status)
            .ToListAsync(cancellationToken);
        int Count(Domain.Attendance.AttendanceStatus status) => statuses.Count(s => s == status);

        return Result<ReportCardDto>.Success(new ReportCardDto(
            studentId, student.DisplayName, org.Name, org.LogoUrl, year.Name, term.Id, term.Name, term.StartDate, term.EndDate, term.ReportCardsPublished,
            classes.Select(c => c.Name).ToList(), org.GradingScale.ToString(), subjects, overall, GradeCalculator.Label(overall, org.GradingScale),
            new ReportAttendanceDto(Count(Domain.Attendance.AttendanceStatus.Present), Count(Domain.Attendance.AttendanceStatus.Absent),
                Count(Domain.Attendance.AttendanceStatus.Late), Count(Domain.Attendance.AttendanceStatus.Excused))));
    }
}
