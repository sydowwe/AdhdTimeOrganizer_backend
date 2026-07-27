using MojaDigitalnaFirma.Kernel.notification;

namespace MojaDigitalnaFirma.Kernel.reminders;

/// <summary>
/// The upsert payload an owning module passes to <see cref="IReminderRegistry.RegisterAsync"/>. Identified
/// by <see cref="Key"/> — re-registering the same key updates the definition in place (idempotent), so an
/// owner re-registers on a date change and cancels on entity deletion. Free of any domain type: recipients
/// are explicit user ids or a resolver key, and text is named by <see cref="TemplateKey"/> +
/// <see cref="Payload"/>, never a domain rule baked in.
/// <para>
/// <b>Text resolution — exactly one of two paths</b> (validated in phase 02, applied in phase 03):
/// <list type="bullet">
/// <item>the owner sets <see cref="NotificationType"/> — a plain Notification type the Notification module
/// renders from <see cref="Payload"/>; or</item>
/// <item>the owner registers an <see cref="IReminderRenderer"/> keyed by <see cref="TemplateKey"/>.</item>
/// </list>
/// This is the only <see cref="TemplateKey"/>→content mechanism — there is no separate mapping table.
/// Phase 02 validates at-least-one path is satisfiable at registration (<see cref="NotificationType"/> set
/// <b>or</b> a renderer registered for <see cref="TemplateKey"/>; <see cref="TemplateKey"/> is always
/// required). Phase 03 resolves at dispatch with a fixed precedence: <see cref="NotificationType"/> wins;
/// the renderer is the fallback used only when it is null (a renderer may be registered after a
/// registration that set <see cref="NotificationType"/>, so both can coexist at dispatch).
/// </para>
/// </summary>
public sealed class ReminderRegistration
{
    /// <summary>The idempotency key (owner + subject + kind).</summary>
    public required ReminderKey Key { get; init; }

    /// <summary>When the reminder occurs.</summary>
    public required ReminderSchedule Schedule { get; init; }

    /// <summary>Stable content key. Always required; resolves to text via <see cref="NotificationType"/> or a keyed <see cref="IReminderRenderer"/>.</summary>
    public required string TemplateKey { get; init; }

    /// <summary>
    /// Payload serialised to JSON and persisted on the definition, fed to the renderer at dispatch time.
    /// Constrained to <see cref="IReminderPayload"/>: it is stored data, so it is bound by the payload PII
    /// contract — ids and non-person scalars, never free-text person data. See
    /// <see cref="MojaDigitalnaFirma.Kernel.notification.payload.INotificationPayload"/> for the rule and why.
    /// </summary>
    public IReminderPayload? Payload { get; init; }

    /// <summary>The Notification type to render from <see cref="Payload"/>, when text isn't supplied by a keyed renderer.</summary>
    public NotificationType? NotificationType { get; init; }

    /// <summary>How recipients are determined.</summary>
    public required RecipientMode RecipientMode { get; init; }

    /// <summary>Explicit recipient user ids. Set only when <see cref="RecipientMode"/> is <see cref="RecipientMode.ExplicitUsers"/>.</summary>
    public IReadOnlyList<long>? ExplicitRecipientUserIds { get; init; }

    /// <summary>The <see cref="IReminderRecipientResolver.Key"/> to resolve recipients with. Set only when <see cref="RecipientMode"/> is <see cref="RecipientMode.ResolverStrategy"/>.</summary>
    public string? RecipientResolverKey { get; init; }

    /// <summary>Optional digest-window key (phase 04): occurrences sharing a key aggregate into one notification. Unused until phase 04.</summary>
    public string? DigestKey { get; init; }

    /// <summary>Optional preferred delivery channel hint (phase 04). Unused until phase 04.</summary>
    public NotificationChannel? ChannelHint { get; init; }
}