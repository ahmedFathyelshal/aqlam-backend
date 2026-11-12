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
    OrganizationEarnings = 4
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
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    public decimal? MonthlyFeeFor(string currency) => currency switch
    {
        "SAR" => MonthlyStudentFeeSar,
        "EGP" => MonthlyStudentFeeEgp,
        _ => null
    };

    /// <summary>Splits a sale: the commission is rounded to the currency's minor unit, the rest is the seller's.</summary>
    public static (decimal Commission, decimal Net) Split(decimal gross, decimal? commissionPercent, decimal providerFee)
    {
        var commission = Math.Round(gross * (commissionPercent ?? 0) / 100m, 2, MidpointRounding.AwayFromZero);
        return (commission, gross - commission - providerFee);
    }
}
