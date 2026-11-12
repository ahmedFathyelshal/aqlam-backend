using AqlamAcademy.Application.Common.Models;
using AqlamAcademy.Domain.Payments;
using FluentValidation;
using MediatR;

namespace AqlamAcademy.Application.Features.Payments;

// ---------- Checkout ----------

public record CheckoutDto(Guid PaymentId, string Status, string RedirectUrl, decimal Amount, string Currency);

public record PaymentStatusDto(Guid PaymentId, string Status, string CourseTitle, decimal Amount, string Currency, string OrganizationSlug, Guid CourseId, string? CoursePublicSlug, string? FailureReason);

/// <summary>What a payment provider tells us about a payment, after its signature was checked.</summary>
public record ProviderEvent(string Provider, string ProviderReference, bool Succeeded, decimal? Amount, string? Currency, decimal ProviderFee, string? FailureReason);

public interface IPaymentService
{
    /// <summary>Starts (or resumes) checkout for the signed-in student in the current organization.</summary>
    Task<Result<CheckoutDto>> CheckoutAsync(Guid courseId, CancellationToken cancellationToken);
    Task<Result<PaymentStatusDto>> GetAsync(Guid paymentId, CancellationToken cancellationToken);
    /// <summary>Applies a provider notification. Safe to call any number of times for the same payment.</summary>
    Task<Result<PaymentStatusDto>> ApplyAsync(ProviderEvent providerEvent, CancellationToken cancellationToken);
    /// <summary>The test provider's pay/decline page, acting for the student who started the payment.</summary>
    Task<Result<PaymentStatusDto>> CompleteTestPaymentAsync(Guid paymentId, bool succeed, CancellationToken cancellationToken);
}

public record CheckoutCommand(Guid CourseId) : IRequest<Result<CheckoutDto>>;

public class CheckoutCommandHandler(IPaymentService payments) : IRequestHandler<CheckoutCommand, Result<CheckoutDto>>
{
    public Task<Result<CheckoutDto>> Handle(CheckoutCommand request, CancellationToken cancellationToken) =>
        payments.CheckoutAsync(request.CourseId, cancellationToken);
}

// ---------- Earnings and the monthly fee ----------

public record MoneyDto(string Currency, decimal Gross, decimal Commission, decimal ProviderFees, decimal Net);

public record SaleDto(Guid PaymentId, DateTime PaidAtUtc, string CourseTitle, string StudentName, decimal Amount, decimal Commission, decimal Net, string Currency);

/// <summary>
/// Story 3.6.1: what an independent teacher would pay this month. Only students the teacher registered themselves
/// and who used the platform this month count; marketplace buyers pay commission instead and are left out.
/// Nothing is charged yet: this is a statement.
/// </summary>
public record MonthlyFeeDto(string Month, int CountedStudents, int ExcludedBuyers, decimal? RatePerStudent, decimal? Amount, string Currency);

public record EarningsDto(
    string OrganizationType,
    decimal? CommissionPercent,
    IReadOnlyList<MoneyDto> Totals,
    IReadOnlyList<SaleDto> RecentSales,
    MonthlyFeeDto? MonthlyFee,
    string Currency = "SAR");

public interface IEarningsService
{
    Task<Result<EarningsDto>> GetAsync(CancellationToken cancellationToken);
}

// ---------- Activation codes ----------

public record CodeBatchDto(Guid Id, Guid CourseId, string CourseTitle, string Name, int Quantity, int Redeemed, DateTime? ExpiresAtUtc, DateTime CreatedAtUtc, DateTime? CancelledAtUtc);

/// <summary>The plain codes are returned only here, once.</summary>
public record CreatedCodeBatchDto(CodeBatchDto Batch, IReadOnlyList<string> Codes);

public record CodeLookupDto(string Status, string LastFour, Guid BatchId, string BatchName, string? RedeemedByUserName, DateTime? RedeemedAtUtc);

public record RedeemedDto(string OrganizationSlug, Guid CourseId, string CourseTitle);

public interface IActivationCodeService
{
    Task<Result<CreatedCodeBatchDto>> CreateBatchAsync(Guid courseId, string name, int quantity, DateTime? expiresAtUtc, CancellationToken cancellationToken);
    Task<Result<IReadOnlyList<CodeBatchDto>>> ListBatchesAsync(Guid courseId, CancellationToken cancellationToken);
    Task<Result<CodeBatchDto>> CancelBatchAsync(Guid courseId, Guid batchId, CancellationToken cancellationToken);
    Task<Result<CodeLookupDto>> LookupAsync(Guid courseId, string code, CancellationToken cancellationToken);
    Task<Result<RedeemedDto>> RedeemAsync(string code, Guid? courseId, CancellationToken cancellationToken);
}

public record CreateCodeBatchCommand(Guid CourseId, string Name, int Quantity, DateTime? ExpiresAtUtc) : IRequest<Result<CreatedCodeBatchDto>>;

public class CreateCodeBatchCommandValidator : AbstractValidator<CreateCodeBatchCommand>
{
    public CreateCodeBatchCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(120);
        RuleFor(x => x.Quantity).InclusiveBetween(1, ActivationCodeBatch.MaxQuantity);
        RuleFor(x => x.ExpiresAtUtc).GreaterThan(_ => DateTime.UtcNow).When(x => x.ExpiresAtUtc is not null)
            .WithMessage("The expiry date must be in the future.");
    }
}

public class CreateCodeBatchCommandHandler(IActivationCodeService codes) : IRequestHandler<CreateCodeBatchCommand, Result<CreatedCodeBatchDto>>
{
    public Task<Result<CreatedCodeBatchDto>> Handle(CreateCodeBatchCommand request, CancellationToken cancellationToken) =>
        codes.CreateBatchAsync(request.CourseId, request.Name.Trim(), request.Quantity, request.ExpiresAtUtc, cancellationToken);
}

public record RedeemCodeCommand(string Code, Guid? CourseId) : IRequest<Result<RedeemedDto>>;

public class RedeemCodeCommandValidator : AbstractValidator<RedeemCodeCommand>
{
    public RedeemCodeCommandValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(40);
    }
}

public class RedeemCodeCommandHandler(IActivationCodeService codes) : IRequestHandler<RedeemCodeCommand, Result<RedeemedDto>>
{
    public Task<Result<RedeemedDto>> Handle(RedeemCodeCommand request, CancellationToken cancellationToken) =>
        codes.RedeemAsync(request.Code, request.CourseId, cancellationToken);
}
