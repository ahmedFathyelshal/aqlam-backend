using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AqlamAcademy.Application.Features.Payments;
using AqlamAcademy.Domain.Payments;
using Microsoft.Extensions.Options;

namespace AqlamAcademy.Infrastructure.Persistence.Payments;

public class PaymentSettings
{
    public const string SectionName = "Payments";

    /// <summary>
    /// A stand-in provider for development, tests and demos: the student sees a pay/decline page instead of a card form.
    /// No money moves. Off unless switched on, so it can never run in production by accident.
    /// </summary>
    public TestProviderSettings TestProvider { get; set; } = new();
}

public class TestProviderSettings
{
    public bool Enabled { get; set; }
    /// <summary>Signs notifications sent to the test webhook, the way a real provider signs its webhooks.</summary>
    public string WebhookSecret { get; set; } = string.Empty;
}

/// <summary>Where the student is sent to pay, and the provider's id for the payment.</summary>
public record ProviderCheckout(string ProviderReference, string RedirectUrl);

/// <summary>
/// One payment provider (Moyasar, Tap, HyperPay, Paymob…). Real providers are added behind this interface once a
/// merchant account and the Saudi partner exist; the seller's country decides which one is used.
/// </summary>
public interface IPaymentProvider
{
    string Name { get; }
    bool Supports(string currency);
    ProviderCheckout Start(Payment payment);
    /// <summary>A verified notification, or null when the signature or body is not valid.</summary>
    ProviderEvent? ParseNotification(string body, string? signature);
}

public sealed class TestPaymentProvider(IOptions<PaymentSettings> settings) : IPaymentProvider
{
    public const string ProviderName = "test";
    public string Name => ProviderName;
    public bool Supports(string currency) => true;

    public ProviderCheckout Start(Payment payment) =>
        new($"test_{payment.Id:N}", $"/checkout/test/{payment.Id}");

    public ProviderEvent? ParseNotification(string body, string? signature)
    {
        var secret = settings.Value.TestProvider.WebhookSecret;
        if (string.IsNullOrEmpty(secret) || string.IsNullOrEmpty(signature) || !SignatureMatches(body, signature, secret))
            return null;
        try
        {
            var message = JsonSerializer.Deserialize<TestNotification>(body, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            return message is null || string.IsNullOrEmpty(message.Reference)
                ? null
                : new ProviderEvent(ProviderName, message.Reference, message.Status == "paid", message.Amount, message.Currency, 0m,
                    message.Status == "paid" ? null : message.Reason ?? "Declined");
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static string Sign(string body, string secret) =>
        Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(body))).ToLowerInvariant();

    private static bool SignatureMatches(string body, string signature, string secret) =>
        CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(Sign(body, secret)), Encoding.ASCII.GetBytes(signature.Trim().ToLowerInvariant()));

    private record TestNotification(string Reference, string Status, decimal? Amount, string? Currency, string? Reason);
}

/// <summary>The providers switched on for this deployment.</summary>
public class PaymentProviders(IEnumerable<IPaymentProvider> all, IOptions<PaymentSettings> settings)
{
    private readonly List<IPaymentProvider> _enabled = all
        .Where(p => p.Name != TestPaymentProvider.ProviderName || settings.Value.TestProvider.Enabled)
        .ToList();

    public bool OnlinePaymentAvailable => _enabled.Count > 0;
    public bool TestProviderEnabled => _enabled.Any(p => p.Name == TestPaymentProvider.ProviderName);

    public IPaymentProvider? ForCurrency(string currency) => _enabled.FirstOrDefault(p => p.Supports(currency));
    public IPaymentProvider? ByName(string name) => _enabled.FirstOrDefault(p => p.Name == name);
}
