using AqlamAcademy.Domain.Enums;

namespace AqlamAcademy.Domain.Payments;

public enum PaymentStatus
{
    Pending = 1,
    Succeeded = 2,
    Failed = 3
}

/// <summary>
/// A student's purchase of one course. Price, title and access rules are copied when checkout starts, so a price
/// change during payment does not change what the student pays or gets (story 3.2.1).
/// </summary>
public class Payment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    /// <summary>The student who gets the course.</summary>
    public Guid UserId { get; set; }
    /// <summary>A linked parent who paid for the student; null when the student paid.</summary>
    public Guid? PaidByUserId { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid CourseId { get; set; }
    public string CourseTitle { get; set; } = string.Empty;
    public AccessExpiryPolicy? AccessExpiryPolicy { get; set; }
    public int? AccessDurationDays { get; set; }

    public decimal Amount { get; set; }
    public string Currency { get; set; } = "SAR";

    /// <summary>Which payment provider handles it ("test" until a real one is connected).</summary>
    public string Provider { get; set; } = string.Empty;
    /// <summary>The provider's id for the payment; unique per provider, used to match its notifications.</summary>
    public string ProviderReference { get; set; } = string.Empty;

    public PaymentStatus Status { get; set; } = PaymentStatus.Pending;
    public string? FailureReason { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAtUtc { get; set; }
}

/// <summary>Where money sits in the ledger.</summary>
public enum LedgerAccount
{
    /// <summary>Money collected by the payment provider on the platform's behalf.</summary>
    ProviderClearing = 1,
    /// <summary>What the platform keeps from a sale.</summary>
    PlatformCommission = 2,
    /// <summary>The provider's charge for the payment.</summary>
    ProviderFees = 3,
    /// <summary>What an organization has earned and will be paid out.</summary>
    OrganizationEarnings = 4,
    /// <summary>Money reserved for a withdrawal request that has not been paid or turned down yet.</summary>
    PayoutsInTransit = 5
}

public enum PayoutStatus
{
    Requested = 1,
    Paid = 2,
    Rejected = 3,
    Cancelled = 4
}

/// <summary>Where an organization wants its earnings sent (bank transfer). One per organization.</summary>
public class PayoutAccount
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public string AccountHolder { get; set; } = string.Empty;
    public string BankName { get; set; } = string.Empty;
    /// <summary>IBAN or account number, stored as typed (spaces removed, upper case).</summary>
    public string Iban { get; set; } = string.Empty;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// A withdrawal request (story 50). The bank details are copied when it is made, so changing them later never
/// redirects a request already in flight. The platform admin pays outside the app and records the result (story 51).
/// </summary>
public class Payout
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public Guid RequestedByUserId { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "SAR";
    public PayoutStatus Status { get; set; } = PayoutStatus.Requested;

    public string AccountHolder { get; set; } = string.Empty;
    public string BankName { get; set; } = string.Empty;
    public string Iban { get; set; } = string.Empty;

    public DateTime RequestedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? ProcessedAtUtc { get; set; }
    public Guid? ProcessedByUserId { get; set; }
    /// <summary>The bank transfer reference, set when paid.</summary>
    public string? TransferReference { get; set; }
    /// <summary>Why it was turned down, shown to the teacher.</summary>
    public string? RejectionReason { get; set; }
}

/// <summary>
/// One line of a double-entry ledger: the lines of one transaction add up to zero. Debits are positive, credits
/// negative, so an organization's earnings balance is minus the sum of its <see cref="LedgerAccount.OrganizationEarnings"/>
/// lines. Lines are never changed; corrections are new transactions.
/// </summary>
public class LedgerEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TransactionId { get; set; }
    public LedgerAccount Account { get; set; }
    /// <summary>Set on organization accounts.</summary>
    public Guid? OrganizationId { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "SAR";
    public Guid? PaymentId { get; set; }
    public Guid? PayoutId { get; set; }
    public string Description { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Platform-wide fees for individual teachers (Feature 3.6). Nothing is set until the platform admin chooses the
/// rates; an unset commission is recorded as zero and an unset monthly fee shows no amount.
/// </summary>
public class PlatformFees
{
    public const int SingletonId = 1;

    public int Id { get; set; } = SingletonId;
    /// <summary>Percent of each marketplace sale kept by the platform.</summary>
    public decimal? CommissionPercent { get; set; }
    /// <summary>Monthly fee for each active student an independent teacher registered themselves, per currency.</summary>
    public decimal? MonthlyStudentFeeSar { get; set; }
    public decimal? MonthlyStudentFeeEgp { get; set; }
    /// <summary>
    /// Age from which a student may pay by themselves (13 to 18). 18 means a linked parent always pays for a minor;
    /// a lower value lets teens whose parents cannot be reached buy for themselves. Changed by the platform admin.
    /// </summary>
    public int MinimumPayingAge { get; set; } = 18;
    /// <summary>Days a sale stays "pending" (not withdrawable) so refunds and disputes can be handled first.</summary>
    public int PayoutHoldDays { get; set; } = 7;
    /// <summary>Smallest withdrawal per currency; null means no minimum.</summary>
    public decimal? MinimumPayoutSar { get; set; }
    public decimal? MinimumPayoutEgp { get; set; }
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    public decimal? MonthlyFeeFor(string currency) => currency switch
    {
        "SAR" => MonthlyStudentFeeSar,
        "EGP" => MonthlyStudentFeeEgp,
        _ => null
    };

    public decimal? MinimumPayoutFor(string currency) => currency switch
    {
        "SAR" => MinimumPayoutSar,
        "EGP" => MinimumPayoutEgp,
        _ => null
    };

    /// <summary>Splits a sale: the commission is rounded to the currency's minor unit, the rest is the seller's.</summary>
    public static (decimal Commission, decimal Net) Split(decimal gross, decimal? commissionPercent, decimal providerFee)
    {
        var commission = Math.Round(gross * (commissionPercent ?? 0) / 100m, 2, MidpointRounding.AwayFromZero);
        return (commission, gross - commission - providerFee);
    }
}
