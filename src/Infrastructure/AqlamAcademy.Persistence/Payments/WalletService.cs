using AqlamAcademy.Application.Common.Interfaces;
using AqlamAcademy.Application.Common.Models;
using AqlamAcademy.Application.Features.Communication;
using AqlamAcademy.Application.Features.Payments;
using AqlamAcademy.Domain.Communication;
using AqlamAcademy.Domain.Payments;
using Microsoft.EntityFrameworkCore;

namespace AqlamAcademy.Infrastructure.Persistence.Payments;

/// <summary>
/// The organization's wallet (stories 46-52). Everything is derived from the append-only ledger: a withdrawal request
/// moves money from earnings to "in transit"; paying it moves it out of the platform; turning it down moves it back.
/// </summary>
public class WalletService(
    ApplicationDbContext platform,
    ICurrentOrganization currentOrganization,
    ICurrentUserService currentUser,
    IAuditLog audit,
    INotifier notifier) : IWalletService, IPayoutAdminService
{
    // ---------- Teacher side ----------

    public async Task<Result<WalletDto>> GetAsync(CancellationToken cancellationToken)
    {
        if (currentOrganization.OrganizationId is not { } orgId)
            return Result<WalletDto>.NotFound("Organization not found.");
        var org = await platform.Organizations.AsNoTracking().FirstAsync(o => o.Id == orgId, cancellationToken);
        var fees = await platform.PlatformFees.AsNoTracking().FirstOrDefaultAsync(cancellationToken);
        var balance = await BalanceAsync(orgId, org.Currency, fees?.PayoutHoldDays ?? 7, cancellationToken);

        var account = await platform.PayoutAccounts.AsNoTracking().FirstOrDefaultAsync(a => a.OrganizationId == orgId, cancellationToken);
        var payouts = await platform.Payouts.AsNoTracking().Where(p => p.OrganizationId == orgId)
            .OrderByDescending(p => p.RequestedAtUtc).Take(50).ToListAsync(cancellationToken);
        var history = payouts.Select(ToDto).ToList();

        return Result<WalletDto>.Success(new WalletDto(org.Currency, balance.Total + balance.InTransit, balance.Pending, balance.Available, balance.InTransit,
            fees?.PayoutHoldDays ?? 7, fees?.MinimumPayoutFor(org.Currency),
            account is null ? null : new PayoutAccountDto(account.AccountHolder, account.BankName, account.Iban),
            history.FirstOrDefault(p => p.Status == nameof(PayoutStatus.Requested)), history));
    }

    public async Task<Result<PayoutAccountDto>> SaveAccountAsync(PayoutAccountDto dto, CancellationToken cancellationToken)
    {
        if (currentOrganization.OrganizationId is not { } orgId)
            return Result<PayoutAccountDto>.NotFound("Organization not found.");
        var account = await platform.PayoutAccounts.FirstOrDefaultAsync(a => a.OrganizationId == orgId, cancellationToken);
        if (account is null)
        {
            account = new PayoutAccount { OrganizationId = orgId };
            platform.PayoutAccounts.Add(account);
        }
        account.AccountHolder = dto.AccountHolder;
        account.BankName = dto.BankName;
        account.Iban = dto.Iban;
        account.UpdatedAtUtc = DateTime.UtcNow;
        try
        {
            await platform.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            return Result<PayoutAccountDto>.Conflict("Your bank details were saved at the same moment from another place. Try again.");
        }
        // Never write the full number to the audit trail.
        await audit.WriteAsync("payout.account.save", currentUser.UserId, null, orgId, $"Bank details saved (ending {Last4(dto.Iban)}).", cancellationToken);
        return Result<PayoutAccountDto>.Success(dto);
    }

    public async Task<Result<PayoutDto>> RequestAsync(decimal amount, CancellationToken cancellationToken)
    {
        if (currentOrganization.OrganizationId is not { } orgId)
            return Result<PayoutDto>.NotFound("Organization not found.");
        var org = await platform.Organizations.AsNoTracking().FirstAsync(o => o.Id == orgId, cancellationToken);
        var fees = await platform.PlatformFees.AsNoTracking().FirstOrDefaultAsync(cancellationToken);
        var account = await platform.PayoutAccounts.AsNoTracking().FirstOrDefaultAsync(a => a.OrganizationId == orgId, cancellationToken);
        if (account is null)
            return Result<PayoutDto>.Failure(["Add your bank details before asking for a payout."]);

        if (await platform.Payouts.AnyAsync(p => p.OrganizationId == orgId && p.Status == PayoutStatus.Requested, cancellationToken))
            return Result<PayoutDto>.Conflict("You already have a payout request waiting. Wait for it to be paid, or cancel it first.");

        var minimum = fees?.MinimumPayoutFor(org.Currency);
        if (minimum is > 0 && amount < minimum)
            return Result<PayoutDto>.Failure([$"The smallest payout is {minimum:0.##} {org.Currency}."]);

        var balance = await BalanceAsync(orgId, org.Currency, fees?.PayoutHoldDays ?? 7, cancellationToken);
        if (amount > balance.Available)
            return Result<PayoutDto>.Failure([$"Only {balance.Available:0.00} {org.Currency} is available to withdraw."]);

        var payout = new Payout
        {
            OrganizationId = orgId,
            RequestedByUserId = currentUser.UserId,
            Amount = amount,
            Currency = org.Currency,
            AccountHolder = account.AccountHolder,
            BankName = account.BankName,
            Iban = account.Iban
        };
        platform.Payouts.Add(payout);
        platform.LedgerEntries.AddRange(Lines(payout, LedgerAccount.OrganizationEarnings, LedgerAccount.PayoutsInTransit, "Payout requested"));
        try
        {
            await platform.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Two requests at once: the unique index let only one through.
            return Result<PayoutDto>.Conflict("You already have a payout request waiting.");
        }
        await audit.WriteAsync("payout.request", currentUser.UserId, null, orgId, $"Requested {amount:0.00} {payout.Currency} (payout {payout.Id}).", cancellationToken);
        return Result<PayoutDto>.Success(ToDto(payout));
    }

    public async Task<Result<PayoutDto>> CancelAsync(Guid payoutId, CancellationToken cancellationToken)
    {
        if (currentOrganization.OrganizationId is not { } orgId)
            return Result<PayoutDto>.NotFound("Organization not found.");
        var payout = await platform.Payouts.FirstOrDefaultAsync(p => p.Id == payoutId && p.OrganizationId == orgId, cancellationToken);
        if (payout is null)
            return Result<PayoutDto>.NotFound("Payout not found.");
        var settled = await SettleAsync(payout, PayoutStatus.Cancelled, null, null, cancellationToken);
        if (!settled)
            return Result<PayoutDto>.Conflict("This request was already handled.");
        await audit.WriteAsync("payout.cancel", currentUser.UserId, null, orgId, $"Cancelled payout {payout.Id}.", cancellationToken);
        return Result<PayoutDto>.Success(ToDto(payout));
    }

    // ---------- Platform admin side ----------

    public async Task<IReadOnlyList<AdminPayoutDto>> ListAsync(string? status, CancellationToken cancellationToken)
    {
        var query = platform.Payouts.AsNoTracking().AsQueryable();
        if (Enum.TryParse<PayoutStatus>(status, true, out var parsed))
            query = query.Where(p => p.Status == parsed);
        var rows = await (from p in query
                          join o in platform.Organizations.AsNoTracking() on p.OrganizationId equals o.Id
                          join u in platform.Users.AsNoTracking() on p.RequestedByUserId equals u.Id
                          orderby p.Status == PayoutStatus.Requested descending, p.RequestedAtUtc descending
                          select new { p, o.Name, o.Slug, u.FullName, u.UserName })
            .Take(200).ToListAsync(cancellationToken);
        return rows.Select(r => new AdminPayoutDto(ToDto(r.p), r.p.OrganizationId, r.Name, r.Slug,
            string.IsNullOrWhiteSpace(r.FullName) ? r.UserName ?? string.Empty : r.FullName)).ToList();
    }

    public async Task<Result<AdminPayoutDto>> MarkPaidAsync(Guid payoutId, string transferReference, CancellationToken cancellationToken) =>
        await ProcessAsync(payoutId, PayoutStatus.Paid, transferReference, null, cancellationToken);

    public async Task<Result<AdminPayoutDto>> RejectAsync(Guid payoutId, string reason, CancellationToken cancellationToken) =>
        await ProcessAsync(payoutId, PayoutStatus.Rejected, null, reason, cancellationToken);

    private async Task<Result<AdminPayoutDto>> ProcessAsync(Guid payoutId, PayoutStatus outcome, string? reference, string? reason, CancellationToken cancellationToken)
    {
        var payout = await platform.Payouts.FirstOrDefaultAsync(p => p.Id == payoutId, cancellationToken);
        if (payout is null)
            return Result<AdminPayoutDto>.NotFound("Payout not found.");
        if (!await SettleAsync(payout, outcome, reference, reason, cancellationToken))
            return Result<AdminPayoutDto>.Conflict("This request was already handled.");

        var org = await platform.Organizations.AsNoTracking().FirstAsync(o => o.Id == payout.OrganizationId, cancellationToken);
        var requester = await platform.Users.AsNoTracking().Where(u => u.Id == payout.RequestedByUserId)
            .Select(u => u.FullName == "" ? u.UserName : u.FullName).FirstOrDefaultAsync(cancellationToken);
        await audit.WriteAsync(outcome == PayoutStatus.Paid ? "payout.paid" : "payout.reject", currentUser.UserId, payout.RequestedByUserId, payout.OrganizationId,
            outcome == PayoutStatus.Paid ? $"Paid payout {payout.Id}: {payout.Amount:0.00} {payout.Currency}, reference {reference}." : $"Rejected payout {payout.Id}: {reason}",
            cancellationToken);

        // Story 52: tell the person who asked, in the app and by email.
        var (title, body) = outcome == PayoutStatus.Paid
            ? ("Your payout was sent", $"{payout.Amount:0.00} {payout.Currency} was transferred to your bank account. Reference: {reference}.")
            : ("Your payout request was declined", $"{payout.Amount:0.00} {payout.Currency} is back in your wallet. Reason: {reason}");
        await notifier.NotifyAsync(new OutgoingNotification([payout.RequestedByUserId], payout.OrganizationId, NotificationType.Payout, title, body,
            $"/teacher/{org.Slug}/earnings", SendEmail: true), cancellationToken);

        return Result<AdminPayoutDto>.Success(new AdminPayoutDto(ToDto(payout), org.Id, org.Name, org.Slug, requester ?? string.Empty));
    }

    // ---------- Ledger ----------

    /// <summary>
    /// Moves an open request to its final state and books the matching ledger lines, once. The status change is
    /// conditional on the request still being open, so a paid-and-cancelled race settles exactly one way.
    /// </summary>
    private async Task<bool> SettleAsync(Payout payout, PayoutStatus outcome, string? reference, string? reason, CancellationToken cancellationToken)
    {
        await using var transaction = await platform.Database.BeginTransactionAsync(cancellationToken);
        var now = DateTime.UtcNow;
        var changed = await platform.Payouts.Where(p => p.Id == payout.Id && p.Status == PayoutStatus.Requested)
            .ExecuteUpdateAsync(s => s
                .SetProperty(p => p.Status, outcome)
                .SetProperty(p => p.ProcessedAtUtc, now)
                .SetProperty(p => p.ProcessedByUserId, outcome == PayoutStatus.Cancelled ? null : currentUser.UserId)
                .SetProperty(p => p.TransferReference, reference)
                .SetProperty(p => p.RejectionReason, reason), cancellationToken);
        if (changed == 0)
            return false;

        platform.LedgerEntries.AddRange(outcome == PayoutStatus.Paid
            ? Lines(payout, LedgerAccount.PayoutsInTransit, LedgerAccount.ProviderClearing, "Payout paid")
            : Lines(payout, LedgerAccount.PayoutsInTransit, LedgerAccount.OrganizationEarnings, outcome == PayoutStatus.Rejected ? "Payout rejected" : "Payout cancelled"));
        await platform.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        payout.Status = outcome;
        payout.ProcessedAtUtc = now;
        payout.TransferReference = reference;
        payout.RejectionReason = reason;
        return true;
    }

    /// <summary>A debit on <paramref name="debit"/> and an equal credit on <paramref name="credit"/>; organization accounts carry the organization.</summary>
    private static LedgerEntry[] Lines(Payout payout, LedgerAccount debit, LedgerAccount credit, string description)
    {
        var transactionId = Guid.NewGuid();
        LedgerEntry Line(LedgerAccount account, decimal amount) => new()
        {
            TransactionId = transactionId,
            Account = account,
            Amount = amount,
            OrganizationId = account is LedgerAccount.OrganizationEarnings or LedgerAccount.PayoutsInTransit ? payout.OrganizationId : null,
            Currency = payout.Currency,
            PayoutId = payout.Id,
            Description = description
        };
        return [Line(debit, payout.Amount), Line(credit, -payout.Amount)];
    }

    private record Balance(decimal Total, decimal Pending, decimal Available, decimal InTransit);

    /// <summary>Total is what the organization is owed; pending is the part earned by sales still inside the hold period.</summary>
    private async Task<Balance> BalanceAsync(Guid orgId, string currency, int holdDays, CancellationToken cancellationToken)
    {
        var mine = platform.LedgerEntries.AsNoTracking().Where(l => l.OrganizationId == orgId && l.Currency == currency);
        var total = -await mine.Where(l => l.Account == LedgerAccount.OrganizationEarnings).SumAsync(l => (decimal?)l.Amount, cancellationToken) ?? 0m;
        var inTransit = -await mine.Where(l => l.Account == LedgerAccount.PayoutsInTransit).SumAsync(l => (decimal?)l.Amount, cancellationToken) ?? 0m;

        var since = DateTime.UtcNow.AddDays(-holdDays);
        var pending = -await (from l in mine.Where(l => l.Account == LedgerAccount.OrganizationEarnings)
                              join p in platform.Payments.AsNoTracking() on l.PaymentId equals p.Id
                              where p.CompletedAtUtc > since
                              select (decimal?)l.Amount).SumAsync(cancellationToken) ?? 0m;
        pending = Math.Min(Math.Max(pending, 0m), Math.Max(total, 0m));
        return new Balance(total, pending, Math.Max(total - pending, 0m), inTransit);
    }

    private static PayoutDto ToDto(Payout p) => new(p.Id, p.Status.ToString(), p.Amount, p.Currency, p.RequestedAtUtc, p.ProcessedAtUtc,
        p.TransferReference, p.RejectionReason, new PayoutAccountDto(p.AccountHolder, p.BankName, p.Iban));

    private static string Last4(string iban) => iban.Length <= 4 ? iban : iban[^4..];
}
