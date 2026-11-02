using System.Net;
using System.Net.Mail;
using System.Threading.Channels;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AqlamAcademy.Infrastructure.Persistence.Messaging;

public class EmailSettings
{
    public const string SectionName = "Email";

    /// <summary>When empty, emails are only written to the log (development, tests).</summary>
    public string? SmtpHost { get; set; }
    public int SmtpPort { get; set; } = 587;
    public bool EnableSsl { get; set; } = true;
    public string? UserName { get; set; }
    public string? Password { get; set; }
    public string FromAddress { get; set; } = "no-reply@aqlam.local";
    public string FromName { get; set; } = "Aqlam";
}

public record EmailMessage(string To, string Subject, string Body);

public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken);
}

/// <summary>Development fallback: records what would have been sent.</summary>
public class LoggingEmailSender(ILogger<LoggingEmailSender> logger) : IEmailSender
{
    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        logger.LogInformation("Email (not sent, no SMTP configured) to {To}: {Subject}", message.To, message.Subject);
        return Task.CompletedTask;
    }
}

public class SmtpEmailSender(IOptions<EmailSettings> options) : IEmailSender
{
    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        using var client = new SmtpClient(settings.SmtpHost, settings.SmtpPort) { EnableSsl = settings.EnableSsl };
        if (!string.IsNullOrEmpty(settings.UserName))
            client.Credentials = new NetworkCredential(settings.UserName, settings.Password);

        using var mail = new MailMessage(new MailAddress(settings.FromAddress, settings.FromName), new MailAddress(message.To))
        {
            Subject = message.Subject,
            Body = message.Body,
            BodyEncoding = System.Text.Encoding.UTF8,
            SubjectEncoding = System.Text.Encoding.UTF8
        };
        await client.SendMailAsync(mail, cancellationToken);
    }
}

/// <summary>
/// In-memory queue so requests never wait for SMTP. Emails still queued when the process stops are lost;
/// in-app notifications are the durable record (a persistent outbox comes with the background-jobs enabler).
/// </summary>
public class EmailQueue
{
    private readonly Channel<EmailMessage> _channel = Channel.CreateBounded<EmailMessage>(
        new BoundedChannelOptions(10_000) { FullMode = BoundedChannelFullMode.DropOldest });

    public void Enqueue(EmailMessage message) => _channel.Writer.TryWrite(message);

    public IAsyncEnumerable<EmailMessage> ReadAllAsync(CancellationToken cancellationToken) => _channel.Reader.ReadAllAsync(cancellationToken);
}

public class EmailDispatcher(EmailQueue queue, IEmailSender sender, ILogger<EmailDispatcher> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var message in queue.ReadAllAsync(stoppingToken))
        {
            try
            {
                await sender.SendAsync(message, stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Could not send email to {To}", message.To);
            }
        }
    }
}
