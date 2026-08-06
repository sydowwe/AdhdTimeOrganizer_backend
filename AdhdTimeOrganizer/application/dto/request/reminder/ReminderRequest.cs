using System.ComponentModel.DataAnnotations;
using AdhdTimeOrganizer.application.service.reminder;
using AdhdTimeOrganizer.domain.model.entity.reminder;
using AdhdTimeOrganizer.domain.model.@enum;
using Sydowwe.Framework.application.dto.request.@interface;

namespace AdhdTimeOrganizer.application.dto.request.reminder;

/// <summary>
/// Create/update payload for a personal reminder. Note this carries no status or next-occurrence field —
/// that state belongs to the Reminders module and is never authored from here.
/// </summary>
public record ReminderRequest : IMyRequest<Reminder>
{
    [StringLength(200)]
    public required string Title { get; init; }

    [StringLength(1000)]
    public string? Note { get; init; }

    /// <summary>
    /// The absolute instant (UTC) the thing happens — for a recurring reminder, the anchor it repeats from.
    /// The client sends an instant, not a wall-clock time: the portal cannot guess which zone a bare local
    /// time was meant in, and reminders are compared against "now".
    /// </summary>
    public required DateTime RemindAt { get; init; }

    /// <summary>
    /// Occurrences relative to <see cref="RemindAt"/>, in minutes, all <c>&lt;= 0</c>. <c>[-10, 0]</c> is
    /// "ten minutes before, then again at the time".
    /// <para>
    /// <b>Omit it</b> (send null) to take the user's default rather than a hard-coded one: for a reminder
    /// attached to a planner task that is <c>UserPlannerSettings.ReminderMinutesBefore</c>; otherwise it fires
    /// exactly at <see cref="RemindAt"/>. An explicit empty list means the same as <c>[0]</c> — it is only the
    /// absent value that asks for the default.
    /// </para>
    /// </summary>
    public List<int>? LeadOffsetsMinutes { get; init; }

    /// <summary>Null fires once. Otherwise the cadence, repeating from <see cref="RemindAt"/>.</summary>
    public ReminderRecurrence? Recurrence { get; init; }

    /// <summary>
    /// Attach the reminder to a planner task, making its instant track the task's start time. Null leaves it
    /// standalone. Only the caller's own tasks are reachable — the endpoint checks through the user-scoped
    /// query filter, so another user's task id reads as "not found".
    /// </summary>
    public long? PlannerTaskId { get; init; }

    public Reminder ToEntity => new()
    {
        UserId = 0,
        Title = Title,
        Note = Note,
        RemindAt = NormalizedRemindAt,
        LeadOffsetsMinutes = NormalizedOffsets,
        Recurrence = Recurrence,
        PlannerTaskId = PlannerTaskId
    };

    public void UpdateEntity(Reminder entity)
    {
        entity.Title = Title;
        entity.Note = Note;
        entity.RemindAt = NormalizedRemindAt;
        entity.LeadOffsetsMinutes = NormalizedOffsets;
        entity.Recurrence = Recurrence;
        entity.PlannerTaskId = PlannerTaskId;
    }

    /// <summary>
    /// Pinned to UTC kind. A JSON instant without an offset deserializes as <c>Unspecified</c>, which Npgsql
    /// refuses to write to a <c>timestamptz</c> column — and letting it fall back to the server's local zone
    /// would silently shift every reminder on a non-UTC host.
    /// </summary>
    private DateTime NormalizedRemindAt => ReminderRegistrationService.AsUtc(RemindAt).UtcDateTime;

    /// <summary>
    /// The offsets as stored. A null or empty list becomes <c>[0]</c> here; the endpoint replaces that with the
    /// user's configured lead time when the request omitted the field <i>and</i> the reminder tracks a task —
    /// it needs a DB read, which a DTO has no business doing.
    /// </summary>
    private List<int> NormalizedOffsets
    {
        get
        {
            var offsets = (LeadOffsetsMinutes ?? []).Distinct().OrderBy(o => o).ToList();
            return offsets.Count > 0 ? offsets : [0];
        }
    }
}