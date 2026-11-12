using AqlamAcademy.Application.Common.Interfaces;
using AqlamAcademy.Application.Common.Models;
using AqlamAcademy.Application.Features.Payments;
using AqlamAcademy.Domain.Enums;
using AqlamAcademy.Domain.Learning;
using AqlamAcademy.Domain.Organizations;
using AqlamAcademy.Domain.Payments;
using AqlamAcademy.Infrastructure.Persistence.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AqlamAcademy.Infrastructure.Persistence.Payments;

/// <summary>
/// Course purchases (story 3.2.1) and the commission split (3.2.2). Access is granted only when the provider
/// confirms the payment; the confirmation may arrive many times, and is booked once.
/// </summary>
public class PaymentService(
    ApplicationDbContext platform,
    TenantDbContext tenant,
    ICurrentUserService currentUser,
    ICurrentOrganization currentOrganization,
    IMembershipService memberships,
    PaymentProviders providers,
    ILogger<PaymentService> logger) : IPaymentService
{
    public async Task<Result<CheckoutDto>> CheckoutAsync(Guid courseId, CancellationToken cancellationToken)
    {
        if (currentOrganization.OrganizationId is not { } orgId)
            return Result<CheckoutDto>.NotFound("Organization not found.");
        var org = await platform.Organizations.AsNoTracking().FirstAsync(o => o.Id == orgId, cancellationToken);

        var course = await tenant.Courses.AsNoTracking().FirstOrDefaultAsync(c => c.Id == courseId, cancellationToken);
        if (course is null || course.Visibility == CourseVisibility.Private || course.Status is not (CourseStatus.Published or CourseStatus.Scheduled))
            return Result<CheckoutDto>.NotFound("Course not found.");
        if (course.Status == CourseStatus.Scheduled)
            return Result<CheckoutDto>.Conflict("This course opens soon and cannot be bought yet.");
        if (course.PricingType != PricingType.Paid || course.Price is not > 0)
            return Result<CheckoutDto>.Conflict("This course is free: join it instead.");
        if (course.CodeActivationOnly)
            return Result<CheckoutDto>.Failure(ResultErrorType.Forbidden, "This course is joined with an activation code only.");

        var provider = providers.ForCurrency(org.Currency);
        if (provider is null)
            return Result<CheckoutDto>.Conflict("Online payment is not available yet.");

        var userId = currentUser.UserId;
        var membership = await platform.Memberships.AsNoTracking().FirstOrDefaultAsync(m => m.OrganizationId == orgId && m.UserId == userId, cancellationToken);
        if (membership is { Status: MembershipStatus.Pending or MembershipStatus.Suspended })
            return Result<CheckoutDto>.Failure(ResultErrorType.Forbidden, "Your membership in this organization does not allow new courses.");

        var enrollment = await platform.Enrollments.AsNoTracking().FirstOrDefaultAsync(e => e.UserId == userId && e.CourseId == courseId, cancellationToken);
        if (enrollment?.IsUsable(DateTime.UtcNow) == true)
            return Result<CheckoutDto>.Conflict("You are already enrolled in this course.");

        // A second click resumes the open checkout, unless the price changed since: then a new one starts.
        var open = await platform.Payments.FirstOrDefaultAsync(p => p.UserId == userId && p.CourseId == courseId && p.Status == PaymentStatus.Pending, cancellationToken);
        if (open is not null && open.Amount == course.Price && open.Currency == org.Currency && open.Provider == provider.Name)
            return Result<CheckoutDto>.Success(new CheckoutDto(open.Id, open.Status.ToString(), provider.Start(open).RedirectUrl, open.Amount, open.Currency));
        if (open is not null)
        {
            open.Status = PaymentStatus.Failed;
            open.FailureReason = "Replaced by a new checkout.";
            open.CompletedAtUtc = DateTime.UtcNow;
            await platform.SaveChangesAsync(cancellationToken);
        }

        var payment = new Payment
        {
            UserId = userId,
            OrganizationId = orgId,
            CourseId = courseId,
            CourseTitle = course.Title,
            AccessExpiryPolicy = course.AccessExpiryPolicy,
            AccessDurationDays = course.AccessDurationDays,
            Amount = course.Price.Value,
            Currency = org.Currency,
            Provider = provider.Name
        };
        var checkout = provider.Start(payment);
        payment.ProviderReference = checkout.ProviderReference;
        platform.Payments.Add(payment);
        try
        {
            await platform.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Two clicks at once: the other request's checkout is the open one.
            platform.ChangeTracker.Clear();
            var other = await platform.Payments.AsNoTracking().FirstAsync(p => p.UserId == userId && p.CourseId == courseId && p.Status == PaymentStatus.Pending, cancellationToken);
            return Result<CheckoutDto>.Success(new CheckoutDto(other.Id, other.Status.ToString(), provider.Start(other).RedirectUrl, other.Amount, other.Currency));
        }

        logger.LogInformation("Payment {PaymentId} started for course {CourseId}: {Amount} {Currency} via {Provider}.",
            payment.Id, courseId, payment.Amount, payment.Currency, payment.Provider);
        return Result<CheckoutDto>.Success(new CheckoutDto(payment.Id, payment.Status.ToString(), checkout.RedirectUrl, payment.Amount, payment.Currency));
    }

    public async Task<Result<PaymentStatusDto>> GetAsync(Guid paymentId, CancellationToken cancellationToken)
    {
        var payment = await platform.Payments.AsNoTracking().FirstOrDefaultAsync(p => p.Id == paymentId && p.UserId == currentUser.UserId, cancellationToken);
        return payment is null ? Result<PaymentStatusDto>.NotFound("Payment not found.") : Result<PaymentStatusDto>.Success(await ToDtoAsync(payment, cancellationToken));
    }

    public async Task<Result<PaymentStatusDto>> CompleteTestPaymentAsync(Guid paymentId, bool succeed, CancellationToken cancellationToken)
    {
        if (!providers.TestProviderEnabled)
            return Result<PaymentStatusDto>.NotFound("Payment not found.");
        var payment = await platform.Payments.AsNoTracking().FirstOrDefaultAsync(
            p => p.Id == paymentId && p.UserId == currentUser.UserId && p.Provider == TestPaymentProvider.ProviderName, cancellationToken);
        if (payment is null)
            return Result<PaymentStatusDto>.NotFound("Payment not found.");
        return await ApplyAsync(new ProviderEvent(payment.Provider, payment.ProviderReference, succeed, payment.Amount, payment.Currency, 0m,
            succeed ? null : "Declined on the test payment page."), cancellationToken);
    }

    public async Task<Result<PaymentStatusDto>> ApplyAsync(ProviderEvent providerEvent, CancellationToken cancellationToken)
    {
        var payment = await platform.Payments.FirstOrDefaultAsync(
            p => p.Provider == providerEvent.Provider && p.ProviderReference == providerEvent.ProviderReference, cancellationToken);
        if (payment is null)
            return Result<PaymentStatusDto>.NotFound("Payment not found.");

        logger.LogInformation("Provider {Provider} reported {Outcome} for payment {PaymentId}.",
            providerEvent.Provider, providerEvent.Succeeded ? "success" : "failure", payment.Id);

        // Settled payments never change: repeated or late notifications are acknowledged and ignored.
        if (payment.Status != PaymentStatus.Pending)
            return Result<PaymentStatusDto>.Success(await ToDtoAsync(payment, cancellationToken));

        var now = DateTime.UtcNow;
        var amountMatches = (providerEvent.Amount is null || providerEvent.Amount == payment.Amount)
                            && (providerEvent.Currency is null || providerEvent.Currency == payment.Currency);
        if (!providerEvent.Succeeded || !amountMatches)
        {
            payment.Status = PaymentStatus.Failed;
            payment.FailureReason = amountMatches ? providerEvent.FailureReason ?? "Declined" : "The paid amount does not match the price.";
            payment.CompletedAtUtc = now;
            await platform.SaveChangesAsync(cancellationToken);
            return Result<PaymentStatusDto>.Success(await ToDtoAsync(payment, cancellationToken));
        }

        payment.Status = PaymentStatus.Succeeded;
        payment.CompletedAtUtc = now;
        await BookAsync(payment, providerEvent.ProviderFee, cancellationToken);

        var refused = await StudentMembershipWriter.EnsureAsync(platform, payment.OrganizationId, payment.UserId, MembershipSource.PublicEnrollment, cancellationToken);
        if (refused is null)
        {
            var enrollment = await platform.Enrollments.FirstOrDefaultAsync(e => e.UserId == payment.UserId && e.CourseId == payment.CourseId, cancellationToken);
            EnrollmentWriter.Upsert(platform, enrollment, payment.UserId, payment.OrganizationId, payment.CourseId, payment.CourseTitle,
                payment.AccessExpiryPolicy, payment.AccessDurationDays, EnrollmentSource.Purchase);
        }
        else
        {
            payment.FailureReason = "Paid, but the organization must re-activate the membership before access is given.";
        }

        try
        {
            await platform.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // The same notification was booked by a parallel request (the ledger allows one booking per payment).
            platform.ChangeTracker.Clear();
            var settled = await platform.Payments.AsNoTracking().FirstAsync(p => p.Id == payment.Id, cancellationToken);
            return Result<PaymentStatusDto>.Success(await ToDtoAsync(settled, cancellationToken));
        }
        memberships.Invalidate(payment.UserId, payment.OrganizationId);
        return Result<PaymentStatusDto>.Success(await ToDtoAsync(payment, cancellationToken));
    }

    /// <summary>
    /// Double entry: what the provider holds for us (debit), its fee (debit), the platform's commission (credit) and the
    /// organization's earnings (credit) add up to zero.
    /// </summary>
    private async Task BookAsync(Payment payment, decimal providerFee, CancellationToken cancellationToken)
    {
        var orgRate = await platform.Organizations.AsNoTracking().Where(o => o.Id == payment.OrganizationId).Select(o => o.CommissionPercent).FirstAsync(cancellationToken);
        var platformRate = await platform.PlatformFees.AsNoTracking().Select(f => f.CommissionPercent).FirstOrDefaultAsync(cancellationToken);
        var (commission, net) = PlatformFees.Split(payment.Amount, orgRate ?? platformRate, providerFee);

        var transactionId = Guid.NewGuid();
        LedgerEntry Line(LedgerAccount account, decimal amount, Guid? orgId) => new()
        {
            TransactionId = transactionId,
            Account = account,
            Amount = amount,
            OrganizationId = orgId,
            Currency = payment.Currency,
            PaymentId = payment.Id,
            Description = $"Course sale: {payment.CourseTitle}"
        };
        platform.LedgerEntries.AddRange(
            Line(LedgerAccount.ProviderClearing, payment.Amount - providerFee, null),
            Line(LedgerAccount.ProviderFees, providerFee, null),
            Line(LedgerAccount.PlatformCommission, -commission, null),
            Line(LedgerAccount.OrganizationEarnings, -net, payment.OrganizationId));
    }

    private async Task<PaymentStatusDto> ToDtoAsync(Payment payment, CancellationToken cancellationToken)
    {
        var orgSlug = await platform.Organizations.AsNoTracking().Where(o => o.Id == payment.OrganizationId).Select(o => o.Slug).FirstAsync(cancellationToken);
        var publicSlug = await platform.MarketplaceCourses.AsNoTracking().Where(c => c.CourseId == payment.CourseId).Select(c => c.PublicSlug).FirstOrDefaultAsync(cancellationToken);
        return new PaymentStatusDto(payment.Id, payment.Status.ToString(), payment.CourseTitle, payment.Amount, payment.Currency, orgSlug, payment.CourseId, publicSlug, payment.FailureReason);
    }
}
