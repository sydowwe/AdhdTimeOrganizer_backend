using Microsoft.Extensions.DependencyInjection;
using Sydowwe.Framework.config.dependencyInjection;
using Sydowwe.Framework.domain.serviceContract;

namespace AdhdTimeOrganizer.Notifications.infrastructure.email;

/// <summary>
/// Transport adapter onto the framework's SMTP sender.
/// <para>
/// <see cref="IEmailSenderService"/> is resolved per send rather than injected, because
/// <c>EmailSenderService</c> reads the MAIL_* env vars in its constructor and throws when they are
/// missing. Constructor injection would blow up on every host that never configures SMTP, even
/// though the dispatcher's <see cref="EmailNotificationOptions.IsConfigured"/> guard means we would
/// never have sent anything. Resolving here keeps construction behind that guard.
/// </para>
/// </summary>
public class SmtpNotificationEmailSender(IServiceProvider services) : INotificationEmailSender, IScopedService
{
    public Task SendAsync(string email, string subject, string htmlBody, CancellationToken ct = default) => services.GetRequiredService<IEmailSenderService>().SendEmailAsync(email, subject, htmlBody);
}