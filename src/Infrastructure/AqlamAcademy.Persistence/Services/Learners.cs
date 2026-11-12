using AqlamAcademy.Application.Common.Interfaces;
using AqlamAcademy.Application.Common.Models;
using AqlamAcademy.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace AqlamAcademy.Infrastructure.Persistence.Services;

/// <summary>
/// Who a marketplace action is for. A student acts for themselves; a parent may act for a linked child (join a
/// free course, buy, use a code). Age rules (2026-11-12): students under 18, or students with no birth year, do not
/// pay by themselves; a linked parent pays for them.
/// </summary>
public class Learners(ApplicationDbContext platform, ICurrentUserService currentUser)
{
    public const string MinorMustAskParent = "Students under 18 cannot pay by themselves. Ask a parent to buy the course for you.";

    /// <summary>The learner (the child when <paramref name="forUserId"/> is a linked child), or a failure.</summary>
    public async Task<(Guid LearnerId, Result? Failure)> ResolveAsync(Guid? forUserId, CancellationToken cancellationToken)
    {
        var me = currentUser.UserId;
        if (forUserId is not { } childId || childId == me)
            return (me, null);
        var linked = await platform.ParentLinks.AnyAsync(l => l.ParentUserId == me && l.ChildUserId == childId, cancellationToken);
        return linked
            ? (childId, null)
            : (me, Result<object>.Failure(ResultErrorType.Forbidden, "This student is not linked to your account."));
    }

    /// <summary>Whether the signed-in user may pay for themselves.</summary>
    public async Task<bool> MayPayAsync(CancellationToken cancellationToken)
    {
        var me = currentUser.UserId;
        var birthYear = await platform.Users.AsNoTracking().Where(u => u.Id == me).Select(u => u.BirthYear).FirstOrDefaultAsync(cancellationToken);
        var isStudent = await (from ur in platform.UserRoles
                               join r in platform.Roles on ur.RoleId equals r.Id
                               where ur.UserId == me && r.Name == "Student"
                               select ur).AnyAsync(cancellationToken);
        return AgeRules.MayPay(birthYear, isStudent, DateTime.UtcNow);
    }
}
