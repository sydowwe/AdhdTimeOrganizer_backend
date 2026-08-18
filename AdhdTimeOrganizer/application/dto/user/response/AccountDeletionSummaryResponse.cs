namespace AdhdTimeOrganizer.application.dto.response.user;

/// <summary>
/// What the account is about to take with it, in numbers — the payload behind the SPA's danger-zone card
/// (<c>DangerZoneSection.vue</c>), which without it can only name the *categories* of data that go.
///
/// <para>Every field is optional to the client by design: the card renders the counts it recognises and
/// falls back to its noun-only wording for anything missing, so adding a field here never needs a
/// coordinated release, and a field the client does not know about is inert rather than broken.</para>
///
/// <para>Counts are exact rather than estimated — see <c>GetAccountDeletionSummaryEndpoint</c> for why that
/// costs nothing here — but they are a <b>warning, not a receipt</b>. They are read outside any transaction
/// and are not promised to match what a later export or the deletion itself observes; a heartbeat landing
/// between the two moves the tracking numbers. Do not use this to reconcile an erasure.</para>
/// </summary>
/// <param name="ActivityCount">Activities the user has defined — the spine every other count hangs off.</param>
/// <param name="TrackedSessionCount">Recorded stretches of time (<c>ActivityHistory</c> rows).</param>
/// <param name="TrackedFrom">
/// Calendar date of the earliest recorded session on the <b>user's own</b> clocks, or null when there are
/// none.
/// </param>
/// <param name="TrackedTo">Calendar date of the latest recorded session, same clocks; null when there are none.</param>
/// <param name="TrackedTimeSpanDays">
/// Whole days from <paramref name="TrackedFrom"/> to <paramref name="TrackedTo"/> inclusive — what the card
/// turns into "N months of history". Null when nothing is recorded; 1 for a single day's worth.
/// </param>
/// <param name="AutomaticTrackingEntryCount">
/// Ingested desktop, web-extension and Android tracking rows combined. Usually the largest number here by
/// orders of magnitude, and the one users least expect to exist.
/// </param>
/// <param name="DayPlanCount">Planned days (<c>Calendar</c> rows).</param>
/// <param name="PlannerTaskCount">Tasks placed on those days.</param>
/// <param name="DayTemplateCount">Reusable day templates.</param>
/// <param name="TodoListCount">To-do lists.</param>
/// <param name="TodoItemCount">Items across all of those lists. Steps live inside their item and are not counted separately.</param>
/// <param name="RoutineCount">Routine to-do items, with the streaks recorded against them.</param>
/// <param name="LeisureItemCount">Bucket-list, project and backlog profiles combined — the "leisure items" of the card.</param>
/// <param name="MemoryAnchorCount">Memory anchors written against past activities.</param>
/// <param name="GoogleCalendarLinked">Whether a Google Calendar connection would be revoked with the account.</param>
public sealed record AccountDeletionSummaryResponse(
    int ActivityCount,
    int TrackedSessionCount,
    DateOnly? TrackedFrom,
    DateOnly? TrackedTo,
    int? TrackedTimeSpanDays,
    int AutomaticTrackingEntryCount,
    int DayPlanCount,
    int PlannerTaskCount,
    int DayTemplateCount,
    int TodoListCount,
    int TodoItemCount,
    int RoutineCount,
    int LeisureItemCount,
    int MemoryAnchorCount,
    bool GoogleCalendarLinked);
