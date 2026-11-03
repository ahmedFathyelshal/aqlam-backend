using AqlamAcademy.Application.Common.Models;
using AqlamAcademy.Domain.Communication;
using FluentValidation;
using MediatR;

namespace AqlamAcademy.Application.Features.Communication;

public record NotificationDto(
    Guid Id,
    Guid? OrganizationId,
    string? OrganizationName,
    string Type,
    string Title,
    string Body,
    string? Link,
    DateTime CreatedAtUtc,
    bool IsRead);

public record NotificationPageDto(int UnreadCount, IReadOnlyList<NotificationDto> Items);

/// <summary>A message to deliver to users: always in-app, optionally by email to those who have one.</summary>
public record OutgoingNotification(
    IReadOnlyCollection<Guid> UserIds,
    Guid? OrganizationId,
    NotificationType Type,
    string Title,
    string Body,
    string? Link,
    bool SendEmail);

/// <summary>Writes in-app notifications and queues emails. Callers never wait for email delivery.</summary>
public interface INotifier
{
    Task NotifyAsync(OutgoingNotification notification, CancellationToken cancellationToken);
}

/// <summary>The signed-in user's inbox.</summary>
public interface INotificationInbox
{
    Task<NotificationPageDto> ListAsync(bool unreadOnly, int take, CancellationToken cancellationToken);
    Task<Result<bool>> MarkReadAsync(Guid? notificationId, CancellationToken cancellationToken);
}

public record ListNotificationsQuery(bool UnreadOnly, int Take = 30) : IRequest<Result<NotificationPageDto>>;

public class ListNotificationsQueryHandler(INotificationInbox inbox) : IRequestHandler<ListNotificationsQuery, Result<NotificationPageDto>>
{
    public async Task<Result<NotificationPageDto>> Handle(ListNotificationsQuery request, CancellationToken cancellationToken) =>
        Result<NotificationPageDto>.Success(await inbox.ListAsync(request.UnreadOnly, Math.Clamp(request.Take, 1, 100), cancellationToken));
}

/// <summary>Marks one notification as read, or all of them when the id is null.</summary>
public record MarkNotificationsReadCommand(Guid? NotificationId) : IRequest<Result<bool>>;

public class MarkNotificationsReadCommandHandler(INotificationInbox inbox) : IRequestHandler<MarkNotificationsReadCommand, Result<bool>>
{
    public Task<Result<bool>> Handle(MarkNotificationsReadCommand request, CancellationToken cancellationToken) =>
        inbox.MarkReadAsync(request.NotificationId, cancellationToken);
}

// ---- Announcements ------------------------------------------------------------------------------------------

public record AnnouncementDto(
    Guid Id,
    string Title,
    string Body,
    string Scope,
    int? GradeId,
    Guid? ClassGroupId,
    string? ClassName,
    IReadOnlyList<string> Audience,
    bool SendEmail,
    Guid AuthorUserId,
    string? AuthorName,
    DateTime PublishedAtUtc,
    int RecipientCount);

public interface IAnnouncementService
{
    Task<Result<AnnouncementDto>> PublishAsync(PublishAnnouncementCommand command, CancellationToken cancellationToken);
    Task<IReadOnlyList<AnnouncementDto>> ListAsync(Guid? classGroupId, CancellationToken cancellationToken);
}

public record PublishAnnouncementCommand(
    string Title,
    string Body,
    AnnouncementScope Scope,
    int? GradeId,
    Guid? ClassGroupId,
    IReadOnlyList<AnnouncementAudience> Audience,
    bool SendEmail) : IRequest<Result<AnnouncementDto>>;

public class PublishAnnouncementCommandValidator : AbstractValidator<PublishAnnouncementCommand>
{
    public PublishAnnouncementCommandValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Body).NotEmpty().MaximumLength(5000);
        RuleFor(x => x.Scope).IsInEnum();
        RuleFor(x => x.GradeId).NotNull().When(x => x.Scope == AnnouncementScope.Grade).WithMessage("Choose a grade.");
        RuleFor(x => x.ClassGroupId).NotNull().When(x => x.Scope == AnnouncementScope.Class).WithMessage("Choose a class.");
        RuleFor(x => x.Audience).NotEmpty().WithMessage("Choose who should receive it.");
        RuleForEach(x => x.Audience).IsInEnum().NotEqual(AnnouncementAudience.None);
    }
}

public class PublishAnnouncementCommandHandler(IAnnouncementService announcements) : IRequestHandler<PublishAnnouncementCommand, Result<AnnouncementDto>>
{
    public Task<Result<AnnouncementDto>> Handle(PublishAnnouncementCommand request, CancellationToken cancellationToken) =>
        announcements.PublishAsync(request with { Title = request.Title.Trim(), Body = request.Body.Trim() }, cancellationToken);
}

public record ListAnnouncementsQuery(Guid? ClassGroupId) : IRequest<Result<IReadOnlyList<AnnouncementDto>>>;

public class ListAnnouncementsQueryHandler(IAnnouncementService announcements) : IRequestHandler<ListAnnouncementsQuery, Result<IReadOnlyList<AnnouncementDto>>>
{
    public async Task<Result<IReadOnlyList<AnnouncementDto>>> Handle(ListAnnouncementsQuery request, CancellationToken cancellationToken) =>
        Result<IReadOnlyList<AnnouncementDto>>.Success(await announcements.ListAsync(request.ClassGroupId, cancellationToken));
}
