namespace AdhdTimeOrganizer.Notifications.infrastructure.email;

/// <summary>
/// Sends one rendered notification email. Abstracted so the dispatcher doesn't depend on the concrete
/// SMTP client (and so tests can fake it) — the same seam shape as <c>IWebPushSender</c>.
/// Throws on delivery failure; the dispatcher catches per recipient so one bad mailbox can't kill a batch.
/// </summary>
public interface INotificationEmailSender
{
    Task SendAsync(string email, string subject, string htmlBody, CancellationToken ct = default);
}