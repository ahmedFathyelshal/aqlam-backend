using AqlamAcademy.Application.Common.Authorization;
using AqlamAcademy.Application.Common.Interfaces;
using AqlamAcademy.Application.Common.Models;
using AqlamAcademy.Domain.Organizations;
using AqlamAcademy.Infrastructure.Persistence.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace AqlamAcademy.Infrastructure.Persistence.Services;

public class PersonalDataService(ApplicationDbContext context, UserManager<ApplicationUser> userManager, IAuditLog audit) : IPersonalDataService
{
    public async Task<Result<object>> ExportAsync(Guid actorUserId, Guid userId, CancellationToken cancellationToken)
    {
        var user = await context.Users.IgnoreQueryFilters().AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (user is null || user.IsDeleted)
            return Result<object>.Failure(["User not found."]);

        var memberships = await (from m in context.Memberships.AsNoTracking()
                                 join o in context.Organizations.AsNoTracking() on m.OrganizationId equals o.Id
                                 where m.UserId == userId
                                 select new { School = o.Name, Roles = m.Roles.ToString(), Status = m.Status.ToString(), Joined = m.JoinedAtUtc }).ToListAsync(cancellationToken);
        var enrollments = await context.Enrollments.AsNoTracking().Where(e => e.UserId == userId)
            .Select(e => new { e.CourseTitle, Source = e.Source.ToString(), Status = e.Status.ToString(), Starts = e.StartsAtUtc, Expires = e.ExpiresAtUtc, e.ProgressPercent, LastAccess = e.LastAccessedAtUtc }).ToListAsync(cancellationToken);
        var payments = await context.Payments.AsNoTracking().Where(p => p.UserId == userId || p.PaidByUserId == userId)
            .Select(p => new { p.CourseTitle, p.Amount, p.Currency, Status = p.Status.ToString(), PaidByParent = p.PaidByUserId != null && p.UserId == userId, PaidByYou = p.PaidByUserId == userId, Created = p.CreatedAtUtc, Completed = p.CompletedAtUtc }).ToListAsync(cancellationToken);
        var children = await (from l in context.ParentLinks.AsNoTracking()
                              join u in context.Users.IgnoreQueryFilters().AsNoTracking() on l.ChildUserId equals u.Id
                              where l.ParentUserId == userId
                              select new { u.UserName, Linked = l.CreatedAtUtc }).ToListAsync(cancellationToken);
        var parents = await (from l in context.ParentLinks.AsNoTracking()
                             join u in context.Users.IgnoreQueryFilters().AsNoTracking() on l.ParentUserId equals u.Id
                             where l.ChildUserId == userId
                             select new { u.UserName, Linked = l.CreatedAtUtc }).ToListAsync(cancellationToken);
        var notifications = await context.Notifications.AsNoTracking().Where(n => n.UserId == userId).OrderByDescending(n => n.CreatedAtUtc).Take(1000)
            .Select(n => new { Type = n.Type.ToString(), n.Title, n.Body, Created = n.CreatedAtUtc, Read = n.ReadAtUtc }).ToListAsync(cancellationToken);
        var acceptances = await context.LegalAcceptances.AsNoTracking().Where(a => a.UserId == userId)
            .Select(a => new { a.Document, a.Version, Accepted = a.AcceptedAtUtc, ByParent = a.AcceptedByUserId != null }).ToListAsync(cancellationToken);
        var requests = await context.DataRequests.AsNoTracking().Where(r => r.UserId == userId)
            .Select(r => new { Type = r.Type.ToString(), Status = r.Status.ToString(), r.Note, Created = r.CreatedAtUtc, Handled = r.HandledAtUtc }).ToListAsync(cancellationToken);
        var studentProfile = await context.StudentProfiles.AsNoTracking().Where(p => p.UserId == userId).Select(p => new { p.Grade, p.SchoolName }).FirstOrDefaultAsync(cancellationToken);
        var teacherProfile = await context.TeacherProfiles.AsNoTracking().Where(p => p.UserId == userId).Select(p => new { p.PublicName, p.Headline, p.Bio, p.Specialization, p.City }).FirstOrDefaultAsync(cancellationToken);

        // A person's own download counts as the answer to their open export request.
        var open = await context.DataRequests.Where(r => r.UserId == userId && r.Type == DataRequestType.Export && r.Status == DataRequestStatus.Open).ToListAsync(cancellationToken);
        foreach (var r in open)
        {
            r.Status = DataRequestStatus.Completed;
            r.HandledAtUtc = DateTime.UtcNow;
            r.HandledByUserId = actorUserId;
            r.Note = actorUserId == userId ? "Downloaded by the account holder." : "Exported by the platform administrator.";
        }
        if (open.Count > 0)
            await context.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync("data-export", actorUserId, userId, null, actorUserId == userId ? "Account holder downloaded their data." : "Administrator exported the account's data.", cancellationToken);

        object export = new
        {
            GeneratedAtUtc = DateTime.UtcNow,
            Note = "Learning records kept by a school (attendance, grades, assignments) belong to that school; ask the school for them.",
            Account = new { user.UserName, user.FullName, user.Email, user.PhoneNumber, user.BirthYear, Gender = user.Gender?.ToString(), Created = user.CreatedAtUtc },
            Schools = memberships,
            Courses = enrollments,
            Payments = payments,
            Children = children,
            Parents = parents,
            Notifications = notifications,
            TermsAccepted = acceptances,
            DataRequests = requests,
            StudentProfile = studentProfile,
            TeacherProfile = teacherProfile
        };
        return Result<object>.Success(export);
    }

    public async Task<Result> AnonymizeAsync(Guid actorUserId, Guid userId, CancellationToken cancellationToken)
    {
        if (actorUserId == userId)
            return Result.Failure(["You cannot delete your own administrator account."]);
        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null || user.IsDeleted)
            return Result.Failure(["User not found."]);
        if (await userManager.IsInRoleAsync(user, PlatformRoles.SuperAdmin))
            return Result.Failure(["A platform administrator account cannot be deleted here."]);

        var ownsSchool = await context.Memberships.AnyAsync(m => m.UserId == userId && m.Status == MembershipStatus.Active && (m.Roles & OrganizationRole.Owner) != 0, cancellationToken);
        if (ownsSchool)
            return Result.Failure(["This person owns a school. Transfer or close the school first."]);

        var suffix = userId.ToString("N")[..8];
        user.UserName = $"deleted-{suffix}";
        user.NormalizedUserName = user.UserName.ToUpperInvariant();
        user.Email = null;
        user.NormalizedEmail = null;
        user.PhoneNumber = null;
        user.FullName = string.Empty;
        user.BirthYear = null;
        user.Gender = null;
        user.IsDeleted = true;
        user.DeletedAtUtc = DateTime.UtcNow;
        user.DeletedBy = actorUserId.ToString();
        user.LockoutEnabled = true;
        user.LockoutEnd = DateTimeOffset.MaxValue;
        user.SecurityStamp = Guid.NewGuid().ToString();
        user.PasswordHash = null;
        var update = await userManager.UpdateAsync(user);
        if (!update.Succeeded)
            return Result.Failure(update.Errors.Select(e => e.Description).ToList());

        var tokens = await context.UserRefreshTokens.Where(t => t.UserId == userId && t.Status == TokenStatus.Active).ToListAsync(cancellationToken);
        foreach (var t in tokens) { t.Status = TokenStatus.Revoked; t.ReasonRevoked = "Account deleted."; }
        var memberships = await context.Memberships.Where(m => m.UserId == userId && m.Status != MembershipStatus.Left).ToListAsync(cancellationToken);
        foreach (var m in memberships) m.Status = MembershipStatus.Left;
        context.Notifications.RemoveRange(await context.Notifications.Where(n => n.UserId == userId).ToListAsync(cancellationToken));
        context.ParentInvites.RemoveRange(await context.ParentInvites.Where(i => i.ChildUserId == userId).ToListAsync(cancellationToken));
        context.StudentProfiles.RemoveRange(await context.StudentProfiles.Where(p => p.UserId == userId).ToListAsync(cancellationToken));
        context.TeacherProfiles.RemoveRange(await context.TeacherProfiles.Where(p => p.UserId == userId).ToListAsync(cancellationToken));
        context.TeacherPhotos.RemoveRange(await context.TeacherPhotos.Where(p => p.UserId == userId).ToListAsync(cancellationToken));
        context.MarketplaceTeachers.RemoveRange(await context.MarketplaceTeachers.Where(t => t.TeacherUserId == userId).ToListAsync(cancellationToken));
        await context.SaveChangesAsync(cancellationToken);

        await audit.WriteAsync("user.deleted", actorUserId, userId, null, "Account anonymised on a deletion request; payment and audit records kept without identity.", cancellationToken);
        return Result.Success();
    }
}
