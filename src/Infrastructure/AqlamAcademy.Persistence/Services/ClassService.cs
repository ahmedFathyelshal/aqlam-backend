using AqlamAcademy.Application.Common.Interfaces;
using AqlamAcademy.Application.Common.Models;
using AqlamAcademy.Application.Features.Classes;
using AqlamAcademy.Domain.Classes;
using AqlamAcademy.Domain.Courses;
using AqlamAcademy.Domain.Enums;
using AqlamAcademy.Domain.Organizations;
using Microsoft.EntityFrameworkCore;

namespace AqlamAcademy.Infrastructure.Persistence.Services;

/// <summary>
/// Branches, classes/groups, rosters and teacher assignments. Classes live in the tenant store; people
/// (names, gender, memberships) in the platform store, so the two are joined in memory.
/// </summary>
public class ClassService : IClassService
{
    private readonly TenantDbContext _tenant;
    private readonly ApplicationDbContext _platform;
    private readonly ICurrentUserService _currentUser;
    private readonly ICurrentOrganization _currentOrganization;
    private readonly IMembershipService _memberships;
    private readonly ClassEnrollmentSync _enrollments;

    public ClassService(TenantDbContext tenant, ApplicationDbContext platform, ICurrentUserService currentUser,
        ICurrentOrganization currentOrganization, IMembershipService memberships, ClassEnrollmentSync enrollments)
    {
        _tenant = tenant;
        _platform = platform;
        _currentUser = currentUser;
        _currentOrganization = currentOrganization;
        _memberships = memberships;
        _enrollments = enrollments;
    }

    private Guid OrgId => _currentOrganization.OrganizationId ?? throw new InvalidOperationException("No organization resolved.");

    // ---- Branches -------------------------------------------------------------------------------------------

    public async Task<IReadOnlyList<BranchDto>> ListBranchesAsync(CancellationToken cancellationToken) =>
        (await _tenant.Branches.AsNoTracking().OrderBy(b => b.Name).ToListAsync(cancellationToken)).Select(ToDto).ToList();

    public async Task<Result<BranchDto>> SaveBranchAsync(Guid? id, string name, GenderSegment gender, string? address, CancellationToken cancellationToken)
    {
        var branch = id is null ? new Branch() : await _tenant.Branches.FirstOrDefaultAsync(b => b.Id == id, cancellationToken);
        if (branch is null)
            return Result<BranchDto>.NotFound("Branch not found.");

        if (await _tenant.Branches.AnyAsync(b => b.Id != branch.Id && b.Name.ToLower() == name.ToLower(), cancellationToken))
            return Result<BranchDto>.Conflict("Another branch already has this name.");

        // A campus cannot become single-gender while it hosts classes of the other gender.
        if (id is not null && gender != GenderSegment.Mixed
            && await _tenant.ClassGroups.AnyAsync(c => c.BranchId == id && c.Gender != gender, cancellationToken))
            return Result<BranchDto>.Conflict("This branch has classes for a different gender. Move or change them first.");

        branch.Name = name;
        branch.Gender = gender;
        branch.Address = string.IsNullOrWhiteSpace(address) ? null : address;
        branch.LastModifiedAtUtc = DateTime.UtcNow;
        if (id is null)
            _tenant.Branches.Add(branch);
        await _tenant.SaveChangesAsync(cancellationToken);

        return Result<BranchDto>.Success(ToDto(branch));
    }

    public async Task<Result<bool>> DeleteBranchAsync(Guid id, CancellationToken cancellationToken)
    {
        var branch = await _tenant.Branches.FirstOrDefaultAsync(b => b.Id == id, cancellationToken);
        if (branch is null)
            return Result<bool>.NotFound("Branch not found.");
        if (await _tenant.ClassGroups.AnyAsync(c => c.BranchId == id, cancellationToken))
            return Result<bool>.Conflict("This branch still has classes. Move or delete them first.");

        _tenant.Branches.Remove(branch);
        await _tenant.SaveChangesAsync(cancellationToken);
        return Result<bool>.Success(true);
    }

    // ---- Classes --------------------------------------------------------------------------------------------

    public async Task<IReadOnlyList<ClassSummaryDto>> ListClassesAsync(bool mine, Guid? branchId, CancellationToken cancellationToken)
    {
        var query = _tenant.ClassGroups.AsNoTracking().Include(c => c.Branch).AsQueryable();
        if (branchId is not null)
            query = query.Where(c => c.BranchId == branchId);
        if (mine)
        {
            var me = _currentUser.UserId;
            query = query.Where(c => c.HomeroomTeacherUserId == me || c.Assignments.Any(a => a.TeacherUserId == me));
        }

        var classes = await query.OrderBy(c => c.Name).ToListAsync(cancellationToken);
        var classIds = classes.Select(c => c.Id).ToList();
        var counts = await _tenant.ClassStudents.Where(s => classIds.Contains(s.ClassGroupId))
            .GroupBy(s => s.ClassGroupId).Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, cancellationToken);
        var names = await UserNamesAsync(classes.Where(c => c.HomeroomTeacherUserId is not null).Select(c => c.HomeroomTeacherUserId!.Value), cancellationToken);

        return classes.Select(c => ToSummary(c, counts.GetValueOrDefault(c.Id), names)).ToList();
    }

    public async Task<Result<ClassDetailDto>> GetClassAsync(Guid id, CancellationToken cancellationToken)
    {
        var classGroup = await _tenant.ClassGroups.AsNoTracking()
            .Include(c => c.Branch).Include(c => c.Students).Include(c => c.Assignments)
            .AsSplitQuery()
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (classGroup is null)
            return Result<ClassDetailDto>.NotFound("Class not found.");

        var userIds = classGroup.Students.Select(s => s.UserId)
            .Concat(classGroup.Assignments.Select(a => a.TeacherUserId));
        if (classGroup.HomeroomTeacherUserId is { } homeroom)
            userIds = userIds.Append(homeroom);
        var people = await PeopleAsync(userIds, cancellationToken);
        var names = people.ToDictionary(p => p.Key, p => p.Value.FullName);

        var courseIds = classGroup.Assignments.Select(a => a.CourseId).ToList();
        var courseStatus = await _tenant.Courses.AsNoTracking().Where(c => courseIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => c.Status.ToString(), cancellationToken);

        var students = (
                from s in classGroup.Students
                where people.ContainsKey(s.UserId)
                let p = people[s.UserId]
                orderby p.FullName
                select new ClassStudentDto(s.UserId, p.UserName, p.FullName, p.Gender?.ToString(), s.AddedAtUtc))
            .ToList();

        return Result<ClassDetailDto>.Success(new ClassDetailDto(
            ToSummary(classGroup, classGroup.Students.Count, names),
            students,
            classGroup.Assignments.OrderBy(a => a.SubjectName).Select(a => ToDto(a, people, courseStatus.GetValueOrDefault(a.CourseId))).ToList()));
    }

    public async Task<Result<ClassSummaryDto>> SaveClassAsync(Guid? id, ClassInput input, CancellationToken cancellationToken)
    {
        var classGroup = id is null
            ? new ClassGroup()
            : await _tenant.ClassGroups.Include(c => c.Branch).Include(c => c.Students).FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (classGroup is null)
            return Result<ClassSummaryDto>.NotFound("Class not found.");

        var errors = new List<string>();

        Branch? branch = null;
        if (input.BranchId is { } branchId)
        {
            branch = await _tenant.Branches.FirstOrDefaultAsync(b => b.Id == branchId, cancellationToken);
            if (branch is null)
                errors.Add("The selected branch does not exist.");
            else if (branch.Gender != GenderSegment.Mixed && input.Gender != branch.Gender)
                errors.Add($"The {branch.Name} branch is for {branch.Gender.ToString().ToLowerInvariant()} only.");
        }

        if (input.HomeroomTeacherUserId is { } homeroom && !await HasActiveRoleAsync(homeroom, OrganizationRole.Teacher, cancellationToken))
            errors.Add("The homeroom teacher must be an active teacher of this organization.");

        if (input.CurriculumId is { } curriculumId && !await _platform.Curricula.AnyAsync(c => c.Id == curriculumId, cancellationToken))
            errors.Add("The selected curriculum does not exist.");
        if (input.GradeId is { } gradeId && !await _platform.EducationGrades.AnyAsync(g => g.Id == gradeId, cancellationToken))
            errors.Add("The selected grade does not exist.");

        if (id is not null)
        {
            if (input.Capacity is { } capacity && classGroup.Students.Count > capacity)
                errors.Add($"The class already has {classGroup.Students.Count} students, more than the new capacity.");

            if (input.Gender != GenderSegment.Mixed && input.Gender != classGroup.Gender)
            {
                var probe = new ClassGroup { Gender = input.Gender };
                var genders = await _platform.Users.Where(u => classGroup.Students.Select(s => s.UserId).Contains(u.Id)).Select(u => u.Gender).ToListAsync(cancellationToken);
                if (genders.Any(g => !probe.Accepts(g)))
                    errors.Add("Some students on the roster do not match the new gender of the class.");
            }
        }

        if (await _tenant.ClassGroups.AnyAsync(c => c.Id != classGroup.Id && c.Name.ToLower() == input.Name.ToLower() && c.BranchId == input.BranchId, cancellationToken))
            errors.Add("Another class in this branch already has this name.");

        if (errors.Count > 0)
            return Result<ClassSummaryDto>.Failure(errors);

        classGroup.Name = input.Name;
        classGroup.Kind = input.Kind;
        classGroup.Gender = input.Gender;
        classGroup.BranchId = input.BranchId;
        classGroup.Branch = branch;
        classGroup.CurriculumId = input.CurriculumId;
        classGroup.GradeId = input.GradeId;
        classGroup.HomeroomTeacherUserId = input.HomeroomTeacherUserId;
        classGroup.Capacity = input.Capacity;
        classGroup.Schedule = (input.Schedule ?? []).OrderBy(s => s.Day).ThenBy(s => s.StartTime)
            .Select(s => new WeeklySlot { Day = s.Day, StartTime = s.StartTime, EndTime = s.EndTime }).ToList();
        classGroup.LastModifiedAtUtc = DateTime.UtcNow;
        if (id is null)
            _tenant.ClassGroups.Add(classGroup);
        await _tenant.SaveChangesAsync(cancellationToken);

        var names = await UserNamesAsync(classGroup.HomeroomTeacherUserId is { } h ? [h] : [], cancellationToken);
        return Result<ClassSummaryDto>.Success(ToSummary(classGroup, classGroup.Students.Count, names));
    }

    public async Task<Result<bool>> DeleteClassAsync(Guid id, CancellationToken cancellationToken)
    {
        var classGroup = await _tenant.ClassGroups.Include(c => c.Students).Include(c => c.Assignments).FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (classGroup is null)
            return Result<bool>.NotFound("Class not found.");
        if (classGroup.Students.Count > 0)
            return Result<bool>.Conflict("Remove the students from the class first.");

        // Subject courses are kept (they may hold content worth copying); only the links go.
        _tenant.TeachingAssignments.RemoveRange(classGroup.Assignments);
        _tenant.ClassGroups.Remove(classGroup);
        await _tenant.SaveChangesAsync(cancellationToken);
        return Result<bool>.Success(true);
    }

    // ---- Roster ---------------------------------------------------------------------------------------------

    public async Task<Result<IReadOnlyList<RosterResultDto>>> AddStudentsAsync(Guid classId, IReadOnlyList<string> userNames, CancellationToken cancellationToken)
    {
        var classGroup = await _tenant.ClassGroups.Include(c => c.Students).FirstOrDefaultAsync(c => c.Id == classId, cancellationToken);
        if (classGroup is null)
            return Result<IReadOnlyList<RosterResultDto>>.NotFound("Class not found.");

        var normalized = userNames.Select(u => u.Trim()).Where(u => u.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var upper = normalized.Select(u => u.ToUpperInvariant()).ToList();
        var orgId = OrgId;

        var members = await (
                from u in _platform.Users
                join m in _platform.Memberships on u.Id equals m.UserId
                where upper.Contains(u.NormalizedUserName!) && m.OrganizationId == orgId
                select new { u.Id, u.UserName, u.Gender, m.Roles, m.Status })
            .ToListAsync(cancellationToken);

        var onRoster = classGroup.Students.Select(s => s.UserId).ToHashSet();
        var added = new List<Guid>();
        var results = new List<RosterResultDto>();
        foreach (var userName in normalized)
        {
            var member = members.FirstOrDefault(m => string.Equals(m.UserName, userName, StringComparison.OrdinalIgnoreCase));
            string? error = member switch
            {
                null => "Not a member of this organization.",
                { Status: not MembershipStatus.Active } => "The membership is not active.",
                _ when !member.Roles.HasFlag(OrganizationRole.Student) => "Only students can be added to a class.",
                _ when !classGroup.Accepts(member.Gender) => $"This class is for {classGroup.Gender.ToString().ToLowerInvariant()} only.",
                _ => null
            };
            if (error is not null)
            {
                results.Add(new RosterResultDto(userName, "Failed", error));
                continue;
            }
            if (onRoster.Contains(member!.Id))
            {
                results.Add(new RosterResultDto(userName, "AlreadyInClass", null));
                continue;
            }
            if (classGroup.Capacity is { } capacity && onRoster.Count >= capacity)
            {
                results.Add(new RosterResultDto(userName, "Failed", "The class is full."));
                continue;
            }

            _tenant.ClassStudents.Add(new ClassStudent { ClassGroupId = classId, UserId = member.Id });
            onRoster.Add(member.Id);
            added.Add(member.Id);
            results.Add(new RosterResultDto(userName, "Added", null));
        }

        await _tenant.SaveChangesAsync(cancellationToken);
        await _enrollments.EnrollStudentsInClassCoursesAsync(orgId, classId, added, cancellationToken);

        return Result<IReadOnlyList<RosterResultDto>>.Success(results);
    }

    public async Task<Result<bool>> RemoveStudentAsync(Guid classId, Guid userId, CancellationToken cancellationToken)
    {
        var row = await _tenant.ClassStudents.FirstOrDefaultAsync(s => s.ClassGroupId == classId && s.UserId == userId, cancellationToken);
        if (row is null)
            return Result<bool>.NotFound("The student is not in this class.");

        _tenant.ClassStudents.Remove(row);
        await _tenant.SaveChangesAsync(cancellationToken);

        var courseIds = await _tenant.TeachingAssignments.Where(a => a.ClassGroupId == classId).Select(a => a.CourseId).ToListAsync(cancellationToken);
        await _enrollments.RevokeAsync(OrgId, courseIds, [userId], cancellationToken);
        return Result<bool>.Success(true);
    }

    // ---- Teacher assignments --------------------------------------------------------------------------------

    public async Task<Result<TeachingAssignmentDto>> AssignTeacherAsync(Guid classId, int? subjectId, string? subjectName, Guid teacherUserId, CancellationToken cancellationToken)
    {
        var classGroup = await _tenant.ClassGroups.AsNoTracking().FirstOrDefaultAsync(c => c.Id == classId, cancellationToken);
        if (classGroup is null)
            return Result<TeachingAssignmentDto>.NotFound("Class not found.");

        if (subjectId is not null)
        {
            var subject = await _platform.Subjects.AsNoTracking().FirstOrDefaultAsync(s => s.Id == subjectId, cancellationToken);
            if (subject is null)
                return Result<TeachingAssignmentDto>.Failure(["The selected subject does not exist."]);
            subjectName = string.IsNullOrWhiteSpace(subjectName) ? subject.NameAr : subjectName;
        }

        if (!await HasActiveRoleAsync(teacherUserId, OrganizationRole.Teacher, cancellationToken))
            return Result<TeachingAssignmentDto>.Failure(["The teacher must be an active teacher of this organization."]);

        if (await _tenant.TeachingAssignments.AnyAsync(a => a.ClassGroupId == classId && a.SubjectName.ToLower() == subjectName!.ToLower(), cancellationToken))
            return Result<TeachingAssignmentDto>.Conflict($"{subjectName} already has a teacher in this class. Change the teacher instead.");

        // The subject space: a private course for this class, ready for the teacher to fill and publish.
        // Classification comes from the class (stage derived from its grade) so the publish checklist only asks for content.
        int? stageId = classGroup.GradeId is { } gradeId
            ? await _platform.EducationGrades.Where(g => g.Id == gradeId).Select(g => (int?)g.EducationStageId).FirstOrDefaultAsync(cancellationToken)
            : null;
        var course = new Course
        {
            Title = $"{subjectName} — {classGroup.Name}",
            Slug = Guid.NewGuid().ToString("N"),
            Status = CourseStatus.Draft,
            Visibility = CourseVisibility.Private,
            PricingType = PricingType.Free,
            AccessExpiryPolicy = AccessExpiryPolicy.Unlimited,
            CurriculumId = classGroup.CurriculumId,
            StageId = stageId,
            GradeId = classGroup.GradeId,
            SubjectId = subjectId,
            CreatedByUserId = teacherUserId
        };
        var assignment = new TeachingAssignment
        {
            ClassGroupId = classId,
            SubjectId = subjectId,
            SubjectName = subjectName!,
            TeacherUserId = teacherUserId,
            CourseId = course.Id
        };
        _tenant.Courses.Add(course);
        _tenant.TeachingAssignments.Add(assignment);
        await _tenant.SaveChangesAsync(cancellationToken);

        var people = await PeopleAsync([teacherUserId], cancellationToken);
        return Result<TeachingAssignmentDto>.Success(ToDto(assignment, people, course.Status.ToString()));
    }

    public async Task<Result<TeachingAssignmentDto>> ChangeTeacherAsync(Guid classId, Guid assignmentId, Guid teacherUserId, CancellationToken cancellationToken)
    {
        var assignment = await _tenant.TeachingAssignments.FirstOrDefaultAsync(a => a.Id == assignmentId && a.ClassGroupId == classId, cancellationToken);
        if (assignment is null)
            return Result<TeachingAssignmentDto>.NotFound("Assignment not found.");
        if (!await HasActiveRoleAsync(teacherUserId, OrganizationRole.Teacher, cancellationToken))
            return Result<TeachingAssignmentDto>.Failure(["The teacher must be an active teacher of this organization."]);

        // The course (content, progress, grades) stays with the class; only the teacher changes.
        assignment.TeacherUserId = teacherUserId;
        await _tenant.SaveChangesAsync(cancellationToken);

        var status = await _tenant.Courses.Where(c => c.Id == assignment.CourseId).Select(c => c.Status.ToString()).FirstOrDefaultAsync(cancellationToken);
        return Result<TeachingAssignmentDto>.Success(ToDto(assignment, await PeopleAsync([teacherUserId], cancellationToken), status));
    }

    public async Task<Result<bool>> RemoveAssignmentAsync(Guid classId, Guid assignmentId, CancellationToken cancellationToken)
    {
        var assignment = await _tenant.TeachingAssignments.FirstOrDefaultAsync(a => a.Id == assignmentId && a.ClassGroupId == classId, cancellationToken);
        if (assignment is null)
            return Result<bool>.NotFound("Assignment not found.");

        _tenant.TeachingAssignments.Remove(assignment);
        await _tenant.SaveChangesAsync(cancellationToken);

        var studentIds = await _tenant.ClassStudents.Where(s => s.ClassGroupId == classId).Select(s => s.UserId).ToListAsync(cancellationToken);
        await _enrollments.RevokeAsync(OrgId, [assignment.CourseId], studentIds, cancellationToken);
        return Result<bool>.Success(true);
    }

    // ---- helpers --------------------------------------------------------------------------------------------

    private sealed record Person(string UserName, string FullName, Gender? Gender);

    private async Task<Dictionary<Guid, Person>> PeopleAsync(IEnumerable<Guid> userIds, CancellationToken cancellationToken)
    {
        var ids = userIds.Distinct().ToList();
        return await _platform.Users.AsNoTracking().Where(u => ids.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => new Person(u.UserName!, u.DisplayName, u.Gender), cancellationToken);
    }

    private async Task<Dictionary<Guid, string>> UserNamesAsync(IEnumerable<Guid> userIds, CancellationToken cancellationToken) =>
        (await PeopleAsync(userIds, cancellationToken)).ToDictionary(p => p.Key, p => p.Value.FullName);

    private async Task<bool> HasActiveRoleAsync(Guid userId, OrganizationRole role, CancellationToken cancellationToken) =>
        (await _memberships.GetActiveRolesAsync(userId, OrgId, cancellationToken)).HasFlag(role);

    private static BranchDto ToDto(Branch b) => new(b.Id, b.Name, b.Gender.ToString(), b.Address);

    private static ClassSummaryDto ToSummary(ClassGroup c, int studentCount, IReadOnlyDictionary<Guid, string> names) => new(
        c.Id, c.Name, c.Kind.ToString(), c.Gender.ToString(), c.BranchId, c.Branch?.Name, c.CurriculumId, c.GradeId,
        c.HomeroomTeacherUserId,
        c.HomeroomTeacherUserId is { } h ? names.GetValueOrDefault(h) : null,
        c.Capacity, studentCount,
        c.Schedule.Select(s => new WeeklySlotDto(s.Day, s.StartTime, s.EndTime)).ToList());

    private static TeachingAssignmentDto ToDto(TeachingAssignment a, IReadOnlyDictionary<Guid, Person> people, string? courseStatus)
    {
        people.TryGetValue(a.TeacherUserId, out var teacher);
        return new(a.Id, a.SubjectId, a.SubjectName, a.TeacherUserId, teacher?.UserName, teacher?.FullName, a.CourseId, courseStatus);
    }
}
