using AqlamAcademy.Application.Common.Models;
using AqlamAcademy.Domain.Classes;
using FluentValidation;
using MediatR;

namespace AqlamAcademy.Application.Features.Marketplace;

// ---------- Public read side (no sign-in) ----------

public record CourseCardDto(
    string Slug,
    string Title,
    string? Subtitle,
    string? ThumbnailUrl,
    string OrganizationName,
    string OrganizationType,
    string TeacherName,
    /// <summary>Address of the teacher's public page; null for center courses and unlisted teachers.</summary>
    string? TeacherSlug,
    string CountryCode,
    string Currency,
    bool IsFree,
    decimal? Price,
    bool CodeActivationOnly,
    bool IsComingSoon,
    int LessonCount,
    int TotalMinutes,
    int FreePreviewCount,
    int? CurriculumId,
    int? GradeId,
    int? SubjectId);

public record TeacherCardDto(
    string Slug,
    string Name,
    string? Headline,
    string? City,
    string CountryCode,
    string Gender,
    string? PhotoUrl,
    IReadOnlyList<int> SubjectIds,
    IReadOnlyList<int> GradeIds,
    int CourseCount);

public record PagedDto<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize);

public record CountDto(int Id, int Count);

/// <summary>How many listed courses each filter value has, so empty choices can be hidden.</summary>
public record FacetsDto(IReadOnlyList<CountDto> Subjects, IReadOnlyList<CountDto> Grades, IReadOnlyList<CountDto> Curricula, int Courses, int Teachers);

public record MarketplaceSearch(
    string? Q = null,
    string? Country = null,
    int? CurriculumId = null,
    int? GradeId = null,
    int? SubjectId = null,
    Gender? TeacherGender = null,
    bool? Free = null,
    int Page = 1,
    int PageSize = 12);

/// <summary>A pick by أقلام (story 3.5.4). Never a ranking: the order is the platform's choice.</summary>
public record FeaturedTeacherDto(Guid Id, int? SubjectId, int Order, TeacherCardDto Teacher);

public record TeacherPageDto(TeacherCardDto Teacher, string? Bio, IReadOnlyList<CourseCardDto> Courses);

public record OutlineLessonDto(Guid Id, string Title, string Type, int? DurationInMinutes, bool IsFreePreview);
public record OutlineChapterDto(string Title, IReadOnlyList<OutlineLessonDto> Lessons);

/// <summary>A linked child, for a parent who joins, buys or uses a code on the child's behalf.</summary>
public record ViewerChildDto(Guid UserId, string Name, bool Enrolled, bool HasPendingPayment);

public record CourseViewerDto(
    bool SignedIn,
    bool Enrolled,
    bool IsStaff,
    bool HasPendingPayment,
    /// <summary>A student under 18 (or with no birth year) cannot pay: a linked parent buys for them.</summary>
    bool MustAskParent = false,
    IReadOnlyList<ViewerChildDto>? Children = null);

public record CoursePageDto(
    CourseCardDto Course,
    Guid CourseId,
    string OrganizationSlug,
    string? PromoVideoUrl,
    string? SyllabusRequirements,
    string AccessExpiryPolicy,
    int? AccessDurationDays,
    IReadOnlyList<OutlineChapterDto> Chapters,
    TeacherCardDto? Teacher,
    /// <summary>Whether online payment is switched on; paid courses cannot be bought online otherwise.</summary>
    bool OnlinePaymentAvailable,
    CourseViewerDto Viewer);

public record PreviewLessonDto(Guid Id, string Title, string Type, string? VideoUrl, string? Summary, string? Content, int? DurationInMinutes);

public interface IMarketplaceReader
{
    Task<PagedDto<CourseCardDto>> SearchCoursesAsync(MarketplaceSearch search, CancellationToken cancellationToken);
    Task<PagedDto<TeacherCardDto>> SearchTeachersAsync(MarketplaceSearch search, CancellationToken cancellationToken);
    Task<FacetsDto> FacetsAsync(string? country, CancellationToken cancellationToken);
    Task<IReadOnlyList<FeaturedTeacherDto>> FeaturedAsync(int? subjectId, string? country, CancellationToken cancellationToken);
    Task<Result<TeacherPageDto>> TeacherAsync(string slug, CancellationToken cancellationToken);
    Task<Result<CoursePageDto>> CourseAsync(string slug, CancellationToken cancellationToken);
    Task<Result<PreviewLessonDto>> PreviewAsync(string slug, Guid lessonId, CancellationToken cancellationToken);
    Task<(byte[] Content, string ContentType)?> TeacherPhotoAsync(string slug, CancellationToken cancellationToken);
}

// ---------- The independent teacher's public profile ----------

public record PublicProfileDto(
    string Slug,
    string? PublicName,
    string? Headline,
    string? Bio,
    string? City,
    string? Gender,
    bool IsListed,
    string? PhotoUrl,
    int PublicCourseCount,
    /// <summary>What still stops the profile from being listed.</summary>
    IReadOnlyList<string> MissingForListing);

public interface IPublicProfileService
{
    Task<Result<PublicProfileDto>> GetAsync(CancellationToken cancellationToken);
    Task<Result<PublicProfileDto>> UpdateAsync(UpdatePublicProfileCommand command, CancellationToken cancellationToken);
    Task<Result<PublicProfileDto>> SavePhotoAsync(byte[] content, CancellationToken cancellationToken);
    Task<Result<PublicProfileDto>> RemovePhotoAsync(CancellationToken cancellationToken);
}

public record UpdatePublicProfileCommand(string? PublicName, string? Headline, string? Bio, string? City, Gender? Gender, bool IsListed)
    : IRequest<Result<PublicProfileDto>>;

public class UpdatePublicProfileCommandValidator : AbstractValidator<UpdatePublicProfileCommand>
{
    public UpdatePublicProfileCommandValidator()
    {
        RuleFor(x => x.PublicName).MaximumLength(100);
        RuleFor(x => x.Headline).MaximumLength(160);
        RuleFor(x => x.Bio).MaximumLength(2000);
        RuleFor(x => x.City).MaximumLength(80);
        RuleFor(x => x.Gender).IsInEnum();
        // Visitors filter by teacher gender (girls' schools are a main market), so a listed teacher must state it.
        RuleFor(x => x.PublicName).NotEmpty().When(x => x.IsListed).WithMessage("Add the name visitors will see before showing your profile in search.");
        RuleFor(x => x.Gender).NotNull().When(x => x.IsListed).WithMessage("Choose male or female before showing your profile in search.");
    }
}

public class UpdatePublicProfileCommandHandler(IPublicProfileService profiles) : IRequestHandler<UpdatePublicProfileCommand, Result<PublicProfileDto>>
{
    public Task<Result<PublicProfileDto>> Handle(UpdatePublicProfileCommand request, CancellationToken cancellationToken) =>
        profiles.UpdateAsync(request, cancellationToken);
}

public record SaveTeacherPhotoCommand(byte[] Content) : IRequest<Result<PublicProfileDto>>;

public class SaveTeacherPhotoCommandValidator : AbstractValidator<SaveTeacherPhotoCommand>
{
    public SaveTeacherPhotoCommandValidator()
    {
        RuleFor(x => x.Content).NotEmpty().WithMessage("Choose an image file.")
            .Must(c => c.Length <= Domain.Organizations.OrganizationLogo.MaxBytes).WithMessage("The photo must be 256 KB or smaller.")
            .Must(c => Domain.Organizations.OrganizationLogo.DetectContentType(c) is not null).WithMessage("Use a PNG, JPEG or WebP image.");
    }
}

public class SaveTeacherPhotoCommandHandler(IPublicProfileService profiles) : IRequestHandler<SaveTeacherPhotoCommand, Result<PublicProfileDto>>
{
    public Task<Result<PublicProfileDto>> Handle(SaveTeacherPhotoCommand request, CancellationToken cancellationToken) =>
        profiles.SavePhotoAsync(request.Content, cancellationToken);
}

// ---------- Platform admin: featured teachers and fees ----------

public record FeesDto(decimal? CommissionPercent, decimal? MonthlyStudentFeeSar, decimal? MonthlyStudentFeeEgp);

public record AdminTeacherDto(Guid OrganizationId, string Slug, string Name, string CountryCode, int CourseCount);

public interface IPlatformMarketplaceService
{
    Task<IReadOnlyList<FeaturedTeacherDto>> ListFeaturedAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<AdminTeacherDto>> ListedTeachersAsync(string? q, CancellationToken cancellationToken);
    Task<Result<FeaturedTeacherDto>> FeatureAsync(FeatureTeacherCommand command, CancellationToken cancellationToken);
    Task<Result> UnfeatureAsync(Guid id, CancellationToken cancellationToken);
    Task<FeesDto> GetFeesAsync(CancellationToken cancellationToken);
    Task<FeesDto> SaveFeesAsync(SaveFeesCommand command, CancellationToken cancellationToken);
    Task<Result> SetCommissionAsync(string slug, decimal? commissionPercent, CancellationToken cancellationToken);
}

public record FeatureTeacherCommand(string Slug, int? SubjectId, int Order) : IRequest<Result<FeaturedTeacherDto>>;

public class FeatureTeacherCommandValidator : AbstractValidator<FeatureTeacherCommand>
{
    public FeatureTeacherCommandValidator()
    {
        RuleFor(x => x.Slug).NotEmpty();
        RuleFor(x => x.Order).InclusiveBetween(0, 1000);
    }
}

public class FeatureTeacherCommandHandler(IPlatformMarketplaceService service) : IRequestHandler<FeatureTeacherCommand, Result<FeaturedTeacherDto>>
{
    public Task<Result<FeaturedTeacherDto>> Handle(FeatureTeacherCommand request, CancellationToken cancellationToken) =>
        service.FeatureAsync(request, cancellationToken);
}

public record SaveFeesCommand(decimal? CommissionPercent, decimal? MonthlyStudentFeeSar, decimal? MonthlyStudentFeeEgp) : IRequest<Result<FeesDto>>;

public class SaveFeesCommandValidator : AbstractValidator<SaveFeesCommand>
{
    public SaveFeesCommandValidator()
    {
        RuleFor(x => x.CommissionPercent).InclusiveBetween(0, 100);
        RuleFor(x => x.MonthlyStudentFeeSar).GreaterThanOrEqualTo(0);
        RuleFor(x => x.MonthlyStudentFeeEgp).GreaterThanOrEqualTo(0);
    }
}

public class SaveFeesCommandHandler(IPlatformMarketplaceService service) : IRequestHandler<SaveFeesCommand, Result<FeesDto>>
{
    public async Task<Result<FeesDto>> Handle(SaveFeesCommand request, CancellationToken cancellationToken) =>
        Result<FeesDto>.Success(await service.SaveFeesAsync(request, cancellationToken));
}

public record SetCommissionCommand(string Slug, decimal? CommissionPercent) : IRequest<Result>;

public class SetCommissionCommandValidator : AbstractValidator<SetCommissionCommand>
{
    public SetCommissionCommandValidator()
    {
        RuleFor(x => x.CommissionPercent).InclusiveBetween(0, 100);
    }
}

public class SetCommissionCommandHandler(IPlatformMarketplaceService service) : IRequestHandler<SetCommissionCommand, Result>
{
    public Task<Result> Handle(SetCommissionCommand request, CancellationToken cancellationToken) =>
        service.SetCommissionAsync(request.Slug, request.CommissionPercent, cancellationToken);
}
