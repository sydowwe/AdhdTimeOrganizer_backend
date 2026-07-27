namespace AdhdTimeOrganizer.Notifications.infrastructure.email;

/// <summary>
/// Guard + tuning for the Email notification channel. Bound from the "EmailNotification" config section.
/// Mirrors <see cref="PushNotificationOptions"/>: when the deployment has no SMTP wired up the dispatcher's
/// email branch short-circuits and the SMTP sender is never constructed (its ctor throws on missing env vars).
/// </summary>
public class EmailNotificationOptions
{
    public const string SectionName = "EmailNotification";

    /// <summary>
    /// Every env var <c>EmailSenderService</c>'s constructor reads — all must be present for auto-detection
    /// to pass, so a positive guard means the sender can actually be constructed. Note <c>API_URL</c>: the
    /// sender eagerly builds a logo URL from it in a field initializer, so it throws without it even though
    /// the notification body never uses the logo.
    /// </summary>
    private static readonly string[] RequiredSmtpEnvVars =
        ["MAIL_FROM_EMAIL", "MAIL_SMTP_SERVER", "MAIL_SMTP_PORT", "MAIL_SMTP_USERNAME", "MAIL_SMTP_PASSWORD", "API_URL"];

    /// <summary>
    /// Explicit master switch. Left null (the default) the channel auto-detects from the presence of the
    /// MAIL_* env vars, so a deployment that configures SMTP gets email notifications with no extra config.
    /// Set it to false to keep SMTP for password resets but silence notification email.
    /// </summary>
    public bool? Enabled { get; set; }

    /// <summary>
    /// Absolute URL of the portal, used for the "open in portal" link in the mail body. Falls back to
    /// <c>Application:Domain</c>; the link is omitted entirely when neither is set.
    /// </summary>
    public string PortalUrl { get; set; } = string.Empty;

    /// <summary>
    /// Max parallel sends. Kept low on purpose — the SMTP sender opens a fresh connection per message,
    /// and providers throttle aggressively.
    /// </summary>
    public int MaxConcurrency { get; set; } = 4;

    public bool IsConfigured =>
        Enabled ?? RequiredSmtpEnvVars.All(v => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(v)));
}