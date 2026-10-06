using AdhdTimeOrganizer.Core.application.@event;
using AdhdTimeOrganizer.Core.application.seam;
using AdhdTimeOrganizer.Core.domain.serviceContract;
using AdhdTimeOrganizer.History.domain.model.entity.activityHistory;
using AdhdTimeOrganizer.History.domain.serviceContract;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Sydowwe.Framework.config.dependencyInjection;
using Sydowwe.Framework.domain.helper;

namespace AdhdTimeOrganizer.History.application.service;

/// <summary>
/// History's half of FO2: after a hand-logged row or a timer session commits, tell the completion automation
/// (<c>ActivityTimeRecordedEventHandler</c> in the host) about it, exactly as the desktop heartbeat does for
/// tracked time. Before this the heartbeat was the event's only publisher, so a 25-minute pomodoro on the
/// planned activity left its block reading <i>Not started</i>.
/// </summary>
/// <remarks>
/// <para>
/// <b>One event per (local day, activity).</b> The event names one day and one window, and the handler reads
/// both: the window's start picks the block the user is "in", and its end becomes a completed block's actual
/// end. A session's focus and break rows are different activities with different spans, and a row crossing
/// midnight is time on two days, so each pair gets its own event with its own clipped window. A window ending
/// on the next midnight reads <c>00:00</c>, the event's documented wrap.
/// </para>
/// <para>
/// <b>Day totals are read back, not summed from the rows.</b> The completion thresholds are cumulative over the
/// day, so the figure has to include everything already logged — through the same sink the heartbeat reads,
/// so the two publishers cannot disagree about what "today's time on Coding" is.
/// </para>
/// <para>
/// <b>The item link travels with the total.</b> A row logged from a to-do or routine item carries its id, and
/// the handler's fallback then completes that item instead of guessing among the open items that share the
/// activity. A session stamps the link on its work rows only, so the rest activity never inherits it.
/// </para>
/// <para>
/// <b>Failures are logged, not thrown.</b> The rows are committed before this runs; a 500 now would make the
/// client retry a save that succeeded, and the retry would 409 on the row's unique key.
/// </para>
/// </remarks>
public sealed class ActivityTimeRecordedPublisher(
    IActivityTimeAttributionSink sink,
    IUserTimeZoneResolver timeZones,
    ILogger<ActivityTimeRecordedPublisher> logger) : IActivityTimeRecordedPublisher, IScopedService
{
    public async Task PublishAsync(DbContext db, long userId, IReadOnlyCollection<ActivityHistory> rows, CancellationToken ct)
    {
        if (rows.Count == 0)
            return;

        try
        {
            var timeZone = await timeZones.GetAsync(userId, ct);

            var groups = rows
                .SelectMany(row => LocalSegments(row, timeZone).Select(segment => (Row: row, Segment: segment)))
                .GroupBy(x => (x.Segment.Day, x.Row.ActivityId))
                .OrderBy(g => g.Key.Day)
                .ThenBy(g => g.Min(x => x.Segment.Start));

            foreach (var group in groups)
            {
                var (day, activityId) = group.Key;
                var dayStartUtc = WallClockZone.ToUtc(day, TimeOnly.MinValue, timeZone);
                var dayEndUtc = WallClockZone.ToUtc(day.AddDays(1), TimeOnly.MinValue, timeZone);
                var totalSeconds = await sink.TotalSecondsBetweenAsync(db, userId, activityId, dayStartUtc, dayEndUtc, ct);

                var linked = group
                    .Select(x => x.Row)
                    .FirstOrDefault(r => r.TodoListItemId is not null || r.RoutineTodoListId is not null);

                await new ActivityTimeRecordedEvent(
                        userId,
                        day,
                        TimeOnly.FromDateTime(group.Min(x => x.Segment.Start)),
                        TimeOnly.FromDateTime(group.Max(x => x.Segment.End)),
                        [new ActivityDayTotal(activityId, totalSeconds, linked?.TodoListItemId, linked?.RoutineTodoListId)])
                    .PublishAsync(Mode.WaitForAll, ct);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Failed to publish ActivityTimeRecordedEvent for {RowCount} history rows of user {UserId}",
                rows.Count, userId);
        }
    }

    /// <summary>
    /// The row's span on the user's wall clock, cut at each local midnight it crosses. A zero-length row is one
    /// empty segment on the day it starts.
    /// </summary>
    private static IEnumerable<LocalSegment> LocalSegments(ActivityHistory row, TimeZoneInfo timeZone)
    {
        var start = WallClockZone.FromUtc(AsUtcInstant(row.StartTimestamp), timeZone);
        var end = WallClockZone.FromUtc(AsUtcInstant(row.EndTimestamp), timeZone);

        var day = DateOnly.FromDateTime(start);
        if (end <= start)
        {
            yield return new LocalSegment(day, start, start);
            yield break;
        }

        while (true)
        {
            var dayStart = day.ToDateTime(TimeOnly.MinValue);
            var nextMidnight = day.AddDays(1).ToDateTime(TimeOnly.MinValue);
            yield return new LocalSegment(day, start > dayStart ? start : dayStart, end < nextMidnight ? end : nextMidnight);

            if (end <= nextMidnight)
                yield break;
            day = day.AddDays(1);
        }
    }

    /// <summary>
    /// A row fresh from a request may still carry <see cref="DateTimeKind.Local"/> from an offset-bearing
    /// timestamp; <see cref="WallClockZone.FromUtc"/> would read that as UTC and shift it by the offset.
    /// </summary>
    private static DateTime AsUtcInstant(DateTime value) =>
        value.Kind == DateTimeKind.Local ? value.ToUniversalTime() : value;

    private readonly record struct LocalSegment(DateOnly Day, DateTime Start, DateTime End);
}
