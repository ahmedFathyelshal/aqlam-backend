using AqlamAcademy.Application.Common.Interfaces;
using AqlamAcademy.Application.Common.Models;
using AqlamAcademy.Application.Features.Communication;
using AqlamAcademy.Domain.Communication;
using AqlamAcademy.Infrastructure.Persistence.Messaging;
using Microsoft.EntityFrameworkCore;

namespace AqlamAcademy.Infrastructure.Persistence.Services;

/// <summary>Writes in-app notifications (platform store) and queues emails for recipients with an address.</summary>
public class Notifier : INotifier
{
    private readonly ApplicationDbContext _platform;
    private readonly EmailQueue _emails;

    public Notifier(ApplicationDbContext platform, EmailQueue emails)
    {
        _platform = platform;
        _emails = emails;
    }

    public async Task NotifyAsync(OutgoingNotification n, CancellationToken cancellationToken)
    {
        var userIds = n.UserIds.Distinct().ToList();
        if (userIds.Count == 0)
            return;

        var now = DateTime.UtcNow;
        _platform.Notifications.AddRange(userIds.Select(id => new Notification
        {
            UserId = id,
            OrganizationId = n.OrganizationId,
            Type = n.Type,
            Title = n.Title,
            Body = n.Body,
            Link = n.Link,
            CreatedAtUtc = now
        }));
        await _platform.SaveChangesAsync(cancellationToken);

        if (!n.SendEmail)
            return;

        var addresses = await _platform.Users.AsNoTracking()
            .Where(u => userIds.Contains(u.Id) && u.Email != null && u.Email != "")
            .Select(u => u.Email!)
            .ToListAsync(cancellationToken);
        foreach (var address in addresses)
            _emails.Enqueue(new EmailMessage(address, n.Title, n.Body));
    }
}

public class NotificationInbox : INotificationInbox
{
    private readonly ApplicationDbContext _platform;
    private readonly ICurrentUserService _currentUser;

    public NotificationInbox(ApplicationDbContext platform, ICurrentUserService currentUser)
    {
        _platform = platform;
        _currentUser = currentUser;
    }

    public async Task<NotificationPageDto> ListAsync(bool unreadOnly, int take, CancellationToken cancellationToken)
    {
        var mine = _platform.Notifications.AsNoTracking().Where(n => n.UserId == _currentUser.UserId);
        var unread = await mine.CountAsync(n => n.ReadAtUtc == null, cancellationToken);

        var rows = await (
                from n in unreadOnly ? mine.Where(n => n.ReadAtUtc == null) : mine
                join o in _platform.Organizations.AsNoTracking() on n.OrganizationId equals o.Id into orgs
                from o in orgs.DefaultIfEmpty()
                orderby n.CreatedAtUtc descending
                select new NotificationDto(n.Id, n.OrganizationId, o != null ? o.Name : null, n.Type.ToString(), n.Title, n.Body, n.Link, n.CreatedAtUtc, n.ReadAtUtc != null))
            .Take(take)
            .ToListAsync(cancellationToken);

        return new NotificationPageDto(unread, rows);
    }

    public async Task<Result<bool>> MarkReadAsync(Guid? notificationId, CancellationToken cancellationToken)
    {
        var query = _platform.Notifications.Where(n => n.UserId == _currentUser.UserId && n.ReadAtUtc == null);
        if (notificationId is { } id)
        {
            if (!await _platform.Notifications.AnyAsync(n => n.Id == id && n.UserId == _currentUser.UserId, cancellationToken))
                return Result<bool>.NotFound("Notification not found.");
            query = query.Where(n => n.Id == id);
        }

        await query.ExecuteUpdateAsync(s => s.SetProperty(n => n.ReadAtUtc, DateTime.UtcNow), cancellationToken);
        return Result<bool>.Success(true);
    }
}
