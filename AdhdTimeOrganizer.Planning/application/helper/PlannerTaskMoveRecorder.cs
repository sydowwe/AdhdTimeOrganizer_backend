using AdhdTimeOrganizer.Core.domain.serviceContract;
using AdhdTimeOrganizer.Planning.domain.model.entity;
using AdhdTimeOrganizer.Planning.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.Planning.domain.model.@enum;
using AdhdTimeOrganizer.Planning.domain.service;
using Sydowwe.Framework.domain.helper;

namespace AdhdTimeOrganizer.Planning.application.helper;

/// <summary>
/// Feeds <see cref="PlannerTaskPostponeRule"/> from a write that has already mapped its new values onto a
/// <b>tracked</b> task: the old slot and status are read off the change tracker's original values, so the
/// endpoints need not capture them before mapping. Call it from <c>AfterMapping</c>, before the save.
/// <para>
/// Costs nothing on an edit that neither moves the task nor touches a <c>NotStarted</c> one. Otherwise one read
/// for the two calendar dates and one for the user's zone (memoised per request).
/// </para>
/// </summary>
public static class PlannerTaskMoveRecorder
{
    public static Task RecordPostponementAsync(
        DbContext dbContext, PlannerTask task, IUserTimeZoneResolver timeZones, CancellationToken ct) =>
        RecordPostponementAsync(dbContext, task, timeZones, DateTime.UtcNow, ct);

    /// <inheritdoc cref="RecordPostponementAsync(DbContext,PlannerTask,IUserTimeZoneResolver,CancellationToken)"/>
    /// <param name="dbContext">The context the task is tracked on.</param>
    /// <param name="task">The tracked task, already carrying its new slot.</param>
    /// <param name="timeZones">Resolves the user's zone.</param>
    /// <param name="utcNow">The instant of the write, for a caller holding an injected <c>TimeProvider</c> — FO4's
    /// re-plan apply, which moves many tasks and must judge every one of them against the same clock.</param>
    /// <param name="ct">Cancellation.</param>
    public static async Task RecordPostponementAsync(
        DbContext dbContext, PlannerTask task, IUserTimeZoneResolver timeZones, DateTime utcNow, CancellationToken ct)
    {
        var entry = dbContext.Entry(task);
        var statusBefore = entry.Property(t => t.Status).OriginalValue;
        if (statusBefore != PlannerTaskStatus.NotStarted)
            return;

        var fromStart = entry.Property(t => t.StartTime).OriginalValue;
        var fromCalendarId = entry.Property(t => t.CalendarId).OriginalValue;
        if (fromStart == task.StartTime && fromCalendarId == task.CalendarId)
            return;

        var dates = await dbContext.Set<Calendar>()
            .AsNoTracking()
            .Where(c => c.Id == fromCalendarId || c.Id == task.CalendarId)
            .Select(c => new { c.Id, c.Date })
            .ToDictionaryAsync(c => c.Id, c => c.Date, ct);

        // Both days exist: the old one held the task, and the write path resolved the new one before mapping.
        // A miss would be an unowned calendar id, which the save is about to refuse anyway.
        if (!dates.TryGetValue(fromCalendarId, out var fromDate) || !dates.TryGetValue(task.CalendarId, out var toDate))
            return;

        var userNow = WallClockZone.FromUtc(utcNow, await timeZones.GetAsync(task.UserId, ct));
        PlannerTaskPostponeRule.Apply(task, statusBefore, fromDate, fromStart, toDate, task.StartTime, userNow);
    }
}
