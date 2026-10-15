using AdhdTimeOrganizer.Tracking.domain.helper.unified;
using Sydowwe.Framework.config.dependencyInjection;
using Sydowwe.Framework.Contracts.notification;
using Sydowwe.Framework.Contracts.notification.kind;
using Sydowwe.Framework.Contracts.notification.payload;
using Sydowwe.Framework.domain.@enum;

namespace AdhdTimeOrganizer.Tracking.application.notification;

/// <summary>
/// The notification kinds the Tracking slice raises. The key is stored and on the wire, and is what the client's
/// <c>notificationTypeMeta</c> is keyed on: <b>only add, never rename</b>.
/// </summary>
public static class TrackingNotificationTypes
{
    public const string TrackerSilentKey = "TrackerSilent";

    public static readonly NotificationType TrackerSilent = new(TrackerSilentKey);
}

/// <summary>
/// A tracker that went quiet. <c>Source</c> is the tracker kind's wire name (<see cref="TrackingSourceNames"/>, never a
/// device name the user typed). <c>LastSeenAt</c> is the last report recorded (UTC), for a client that wants to say
/// "since Tuesday" in the user's own zone; null when unknown. Property names are the persisted JSON keys — read
/// <see cref="INotificationPayload"/> first.
/// </summary>
[NotificationPayload(TrackingNotificationTypes.TrackerSilentKey)]
public sealed record TrackerSilentPayload(
    string Source,
    DateTime? LastSeenAt = null) : INotificationPayload;

/// <summary>
/// One of the user's activity trackers (a desktop agent, a browser extension, a phone app) used to report and has gone
/// quiet — uninstalled, signed out, token expired, crashed. None of that errors anywhere: the dashboards just go empty,
/// and the user concludes the feature is broken or that they did nothing. Raised once per silence by
/// <c>TrackerSilenceNotifier</c> (P2-04), and only while the user is around to act on it.
/// <para>
/// Operational, not a reproach: what stopped, and the two things that usually fix it. No "for 2 days" — the payload
/// carries the last report as an instant, and only the client knows the user's zone to word it.
/// </para>
/// </summary>
public sealed class TrackerSilentNotificationKind : NotificationKind<TrackerSilentPayload>, ISingletonService
{
    public override NotificationType Type => TrackingNotificationTypes.TrackerSilent;

    // Not held while away: the rule only fires for a user the app saw within a day, and it is operational, not a nudge.
    // Pushed, not mailed: the user should hear about it, but it is not worth a mail; one who wants it can opt in.

    protected override (string Title, string Body) Render(TrackerSilentPayload? payload, NotificationRenderContext context)
    {
        var source = NonEmpty(payload?.Source);
        if (context.Kit.Language == AvailableLocales.En)
            return ("Tracking went quiet", source switch
            {
                TrackingSourceNames.Desktop =>
                    "The desktop app has stopped sending your activity. Check that it's running and signed in.",
                TrackingSourceNames.WebExtension =>
                    "The browser extension has stopped sending your activity. Check that it's enabled and signed in.",
                TrackingSourceNames.Android =>
                    "The phone app has stopped sending your activity. Check that it's installed and signed in.",
                _ => "One of your trackers has stopped sending your activity. Check that it's running and signed in."
            });

        return ("Sledovanie aktivity stíchlo", source switch
        {
            TrackingSourceNames.Desktop =>
                "Desktopová aplikácia prestala posielať vašu aktivitu. Skontrolujte, či beží a či ste v nej prihlásení.",
            TrackingSourceNames.WebExtension =>
                "Rozšírenie prehliadača prestalo posielať vašu aktivitu. Skontrolujte, či je zapnuté a či ste v ňom prihlásení.",
            TrackingSourceNames.Android =>
                "Aplikácia v telefóne prestala posielať vašu aktivitu. Skontrolujte, či je nainštalovaná a či ste v nej prihlásení.",
            _ => "Jeden z vašich sledovačov prestal posielať aktivitu. Skontrolujte, či beží a či ste v ňom prihlásení."
        });
    }
}
