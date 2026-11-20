using AqlamAcademy.Application.Common.Models;
using FluentValidation;
using MediatR;

namespace AqlamAcademy.Application.Features.Payments;

// ---------- Teacher wallet and withdrawals (Epic 6: stories 46-52) ----------

public record PayoutAccountDto(string AccountHolder, string BankName, string Iban);

public record PayoutDto(
    Guid Id,
    string Status,
    decimal Amount,
    string Currency,
    DateTime RequestedAtUtc,
    DateTime? ProcessedAtUtc,
    string? TransferReference,
    string? RejectionReason,
    PayoutAccountDto Account);

/// <summary>
/// The organization's money in its own currency. Total = Pending + Available + InTransit:
/// pending sales are still inside the hold period, in transit is reserved by an open withdrawal request.
/// </summary>
public record WalletDto(
    string Currency,
    decimal Total,
    decimal Pending,
    decimal Available,
    decimal InTransit,
    int HoldDays,
    decimal? MinimumPayout,
    PayoutAccountDto? Account,
    PayoutDto? OpenRequest,
    IReadOnlyList<PayoutDto> History);

/// <summary>What the platform admin sees: the request plus whose it is.</summary>
public record AdminPayoutDto(PayoutDto Payout, Guid OrganizationId, string OrganizationName, string OrganizationSlug, string RequestedBy);

public interface IWalletService
{
    Task<Result<WalletDto>> GetAsync(CancellationToken cancellationToken);
    Task<Result<PayoutAccountDto>> SaveAccountAsync(PayoutAccountDto account, CancellationToken cancellationToken);
    Task<Result<PayoutDto>> RequestAsync(decimal amount, CancellationToken cancellationToken);
    Task<Result<PayoutDto>> CancelAsync(Guid payoutId, CancellationToken cancellationToken);
}

/// <summary>Platform admin: pay requests by bank transfer outside the app, then record the result.</summary>
public interface IPayoutAdminService
{
    /// <summary><paramref name="status"/> null lists everything, newest first.</summary>
    Task<IReadOnlyList<AdminPayoutDto>> ListAsync(string? status, CancellationToken cancellationToken);
    Task<Result<AdminPayoutDto>> MarkPaidAsync(Guid payoutId, string transferReference, CancellationToken cancellationToken);
    Task<Result<AdminPayoutDto>> RejectAsync(Guid payoutId, string reason, CancellationToken cancellationToken);
}

public record SavePayoutAccountCommand(string AccountHolder, string BankName, string Iban) : IRequest<Result<PayoutAccountDto>>;

public class SavePayoutAccountCommandValidator : AbstractValidator<SavePayoutAccountCommand>
{
    public SavePayoutAccountCommandValidator()
    {
        RuleFor(x => x.AccountHolder).NotEmpty().MaximumLength(120);
        RuleFor(x => x.BankName).NotEmpty().MaximumLength(120);
        // IBANs are 15-34 letters and digits; Egyptian accounts may be a plain number of 10-30 digits.
        RuleFor(x => x.Iban).NotEmpty().Must(v => PayoutRules.NormalizeIban(v) is { Length: >= 10 and <= 34 } n && n.All(char.IsLetterOrDigit))
            .WithMessage("Enter a valid IBAN or account number.");
    }
}

public class SavePayoutAccountCommandHandler(IWalletService wallet) : IRequestHandler<SavePayoutAccountCommand, Result<PayoutAccountDto>>
{
    public Task<Result<PayoutAccountDto>> Handle(SavePayoutAccountCommand request, CancellationToken cancellationToken) =>
        wallet.SaveAccountAsync(new PayoutAccountDto(request.AccountHolder.Trim(), request.BankName.Trim(), PayoutRules.NormalizeIban(request.Iban)), cancellationToken);
}

public record RequestPayoutCommand(decimal Amount) : IRequest<Result<PayoutDto>>;

public class RequestPayoutCommandValidator : AbstractValidator<RequestPayoutCommand>
{
    public RequestPayoutCommandValidator()
    {
        RuleFor(x => x.Amount).GreaterThan(0).PrecisionScale(18, 2, false).WithMessage("Enter an amount with at most two decimals.");
    }
}

public class RequestPayoutCommandHandler(IWalletService wallet) : IRequestHandler<RequestPayoutCommand, Result<PayoutDto>>
{
    public Task<Result<PayoutDto>> Handle(RequestPayoutCommand request, CancellationToken cancellationToken) =>
        wallet.RequestAsync(request.Amount, cancellationToken);
}

public static class PayoutRules
{
    public static string NormalizeIban(string? value) => new string((value ?? string.Empty).Where(c => !char.IsWhiteSpace(c) && c != '-').ToArray()).ToUpperInvariant();
}
