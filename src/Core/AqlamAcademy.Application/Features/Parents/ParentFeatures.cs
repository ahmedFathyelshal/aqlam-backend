using AqlamAcademy.Application.Common.Models;
using AqlamAcademy.Application.Features.Attendance;
using FluentValidation;
using MediatR;

namespace AqlamAcademy.Application.Features.Parents;

public record ParentDto(Guid UserId, string UserName, string FullName, string? PhoneNumber, DateTime LinkedAtUtc);

public record ParentInviteDto(string Code, string ChildName, DateTime ExpiresAtUtc);

public record ChildOrganizationDto(Guid OrganizationId, string Slug, string Name);

public record ChildDto(Guid UserId, string UserName, string FullName, IReadOnlyList<ChildOrganizationDto> Organizations);

public record ChildCourseDto(Guid CourseId, string Title, int ProgressPercent, DateTime? LastAccessedAtUtc);

public record ChildGradeDto(string Kind, Guid CourseId, string? LessonTitle, decimal? Score, decimal MaxScore, decimal? Percent, string Status, DateTime AtUtc);

public record ChildAttendanceDto(int Present, int Absent, int Late, int Excused, IReadOnlyList<AttendanceEntryDto> Recent);

/// <summary>Read-only view of one child inside one organization.</summary>
public record ChildOverviewDto(
    Guid UserId,
    string FullName,
    IReadOnlyList<string> Classes,
    IReadOnlyList<ChildCourseDto> Courses,
    ChildAttendanceDto Attendance,
    IReadOnlyList<ChildGradeDto> Grades);

public interface IParentService
{
    // Organization staff
    Task<Result<IReadOnlyList<ParentDto>>> ListParentsAsync(Guid childUserId, CancellationToken cancellationToken);
    Task<Result<ParentDto>> LinkParentAsync(Guid childUserId, string parentUserName, CancellationToken cancellationToken);
    Task<Result<bool>> UnlinkParentAsync(Guid childUserId, Guid parentUserId, CancellationToken cancellationToken);
    Task<Result<ParentInviteDto>> CreateInviteAsync(Guid childUserId, CancellationToken cancellationToken);

    // Parents
    Task<Result<ChildDto>> RedeemInviteAsync(string code, CancellationToken cancellationToken);
    Task<IReadOnlyList<ChildDto>> MyChildrenAsync(CancellationToken cancellationToken);
    Task<Result<ChildOverviewDto>> ChildOverviewAsync(Guid childUserId, CancellationToken cancellationToken);
}

public record ListParentsQuery(Guid ChildUserId) : IRequest<Result<IReadOnlyList<ParentDto>>>;

public class ListParentsQueryHandler(IParentService parents) : IRequestHandler<ListParentsQuery, Result<IReadOnlyList<ParentDto>>>
{
    public Task<Result<IReadOnlyList<ParentDto>>> Handle(ListParentsQuery r, CancellationToken cancellationToken) =>
        parents.ListParentsAsync(r.ChildUserId, cancellationToken);
}

public record LinkParentCommand(Guid ChildUserId, string ParentUserName) : IRequest<Result<ParentDto>>;

public class LinkParentCommandValidator : AbstractValidator<LinkParentCommand>
{
    public LinkParentCommandValidator() => RuleFor(x => x.ParentUserName).NotEmpty().MaximumLength(256);
}

public class LinkParentCommandHandler(IParentService parents) : IRequestHandler<LinkParentCommand, Result<ParentDto>>
{
    public Task<Result<ParentDto>> Handle(LinkParentCommand r, CancellationToken cancellationToken) =>
        parents.LinkParentAsync(r.ChildUserId, r.ParentUserName.Trim(), cancellationToken);
}

public record UnlinkParentCommand(Guid ChildUserId, Guid ParentUserId) : IRequest<Result<bool>>;

public class UnlinkParentCommandHandler(IParentService parents) : IRequestHandler<UnlinkParentCommand, Result<bool>>
{
    public Task<Result<bool>> Handle(UnlinkParentCommand r, CancellationToken cancellationToken) =>
        parents.UnlinkParentAsync(r.ChildUserId, r.ParentUserId, cancellationToken);
}

public record CreateParentInviteCommand(Guid ChildUserId) : IRequest<Result<ParentInviteDto>>;

public class CreateParentInviteCommandHandler(IParentService parents) : IRequestHandler<CreateParentInviteCommand, Result<ParentInviteDto>>
{
    public Task<Result<ParentInviteDto>> Handle(CreateParentInviteCommand r, CancellationToken cancellationToken) =>
        parents.CreateInviteAsync(r.ChildUserId, cancellationToken);
}

public record RedeemParentInviteCommand(string Code) : IRequest<Result<ChildDto>>;

public class RedeemParentInviteCommandValidator : AbstractValidator<RedeemParentInviteCommand>
{
    public RedeemParentInviteCommandValidator() => RuleFor(x => x.Code).NotEmpty().MaximumLength(32);
}

public class RedeemParentInviteCommandHandler(IParentService parents) : IRequestHandler<RedeemParentInviteCommand, Result<ChildDto>>
{
    public Task<Result<ChildDto>> Handle(RedeemParentInviteCommand r, CancellationToken cancellationToken) =>
        parents.RedeemInviteAsync(r.Code, cancellationToken);
}

public record MyChildrenQuery : IRequest<Result<IReadOnlyList<ChildDto>>>;

public class MyChildrenQueryHandler(IParentService parents) : IRequestHandler<MyChildrenQuery, Result<IReadOnlyList<ChildDto>>>
{
    public async Task<Result<IReadOnlyList<ChildDto>>> Handle(MyChildrenQuery request, CancellationToken cancellationToken) =>
        Result<IReadOnlyList<ChildDto>>.Success(await parents.MyChildrenAsync(cancellationToken));
}

public record ChildOverviewQuery(Guid ChildUserId) : IRequest<Result<ChildOverviewDto>>;

public class ChildOverviewQueryHandler(IParentService parents) : IRequestHandler<ChildOverviewQuery, Result<ChildOverviewDto>>
{
    public Task<Result<ChildOverviewDto>> Handle(ChildOverviewQuery r, CancellationToken cancellationToken) =>
        parents.ChildOverviewAsync(r.ChildUserId, cancellationToken);
}
