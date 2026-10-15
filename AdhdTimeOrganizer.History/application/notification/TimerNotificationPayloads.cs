using AdhdTimeOrganizer.History.domain.model.@enum;
using Sydowwe.Framework.Contracts.notification.payload;

namespace AdhdTimeOrganizer.History.application.notification;

// Read INotificationPayload first: ids and non-person scalars only. Property names are the persisted camelCase JSON
// keys, so renaming one is a storage change — an old row keeps the old key and its kind renders the fallback.

/// <summary>
/// A running timer crossed a phase boundary. Structured rather than a pre-rendered title/body: the text is composed
/// by <see cref="TimerBoundaryNotificationKind"/> from these fields, which keeps the persisted payload
/// locale-agnostic and inside the payload PII contract.
/// <para>
/// <c>ActivityName</c> is the user's own label for their own activity ("Reading") — free text, but <b>their</b> text
/// about <b>their</b> row, and the row it is read from is deleted with their account. Nothing person-shaped about
/// anyone else belongs here. It is null when the timer named no activity, or when the activity was deleted between
/// Start and the boundary.
/// </para>
/// <para>
/// <c>Tag</c> is the browser notification tag and is <b>load-bearing, not decoration</b>: when the tab is still open
/// the page shows its own notification under the same tag, and the push replaces it in place rather than stacking a
/// second one. It must reach the service worker verbatim. Derived from the boundary instant, so a client can rebuild
/// it with no round trip.
/// </para>
/// </summary>
/// <param name="Phase">Which boundary this is; picks the title.</param>
/// <param name="PhaseIndex">1-based index of the period within the cycle, or null for a plain countdown.</param>
/// <param name="CycleIndex">1-based index of the cycle within the preset, or null for a plain countdown.</param>
[NotificationPayload(HistoryNotificationTypes.TimerBoundaryKey)]
public sealed record TimerBoundaryPayload(
    TimerBoundaryPhase Phase,
    string? ActivityName = null,
    int? PhaseIndex = null,
    int? PhaseTotal = null,
    int? CycleIndex = null,
    int? CycleTotal = null,
    string? Tag = null,
    string? Url = null) : INotificationPayload;

/// <summary>
/// A running stopwatch checking in. <c>ElapsedMinutes</c> is the stopwatch's own running time at the check-in, as the
/// client counted it (pauses excluded), not the wall time since Start; 0 when unknown. <c>ActivityName</c> is the
/// user's own label for their own activity — the <see cref="TimerBoundaryPayload"/> rule — and null when the
/// stopwatch named none or the activity is gone.
/// <para>
/// <c>Tag</c> and <c>Url</c> do what <see cref="TimerBoundaryPayload"/>'s do: the tag is derived from the check-in
/// instant so a second copy of the same check-in replaces the first rather than stacking, and the url is the client's
/// own relative route to the stopwatch, which the service worker opens on a tap.
/// </para>
/// </summary>
[NotificationPayload(HistoryNotificationTypes.StopwatchCheckInKey)]
public sealed record StopwatchCheckInPayload(
    int ElapsedMinutes,
    string? ActivityName = null,
    string? Tag = null,
    string? Url = null) : INotificationPayload;
