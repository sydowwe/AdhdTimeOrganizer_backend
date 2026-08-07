using AdhdTimeOrganizer.Reminders.domain.entity;
using Sydowwe.Framework.Contracts.reminders;
using Sydowwe.Framework.Contracts.scheduling;

namespace AdhdTimeOrganizer.Reminders.domain.service;

/// <summary>
/// The single source of truth for "when does this reminder next occur?" — shared by the registry
/// (phase 02, on register/resume) and the scanner (phase 03, to advance a recurring reminder). Both must
/// agree on the <i>due occurrence</i> definition, so the math lives here once and is never duplicated.
/// <para>
/// A "next occurrence" is the soonest occurrence instant <b>at or after</b> <c>from</c> that is not already
/// in the dispatch log and not past a recurring schedule's end. Occurrences already in the past at the call
/// instant are skipped — registering inside a lead window does not retro-fire earlier offsets (no backfill
/// flood); the scanner still catches the small register-to-scan gap via its <c>NextOccurrenceAt &lt;= now</c>
/// seek. Returns <c>null</c> when the schedule has no remaining occurrence (a one-shot whose offsets are all
/// past/dispatched, or a recurring schedule past its end).
/// </para>
/// </summary>
public static class ReminderOccurrenceCalculator
{
    /// <summary>Guards the interval/cron probe loops against a pathological schedule (never an expected path).</summary>
    private const int MaxProbe = 100_000;

    private static readonly IReadOnlySet<DateTimeOffset> NoneDispatched = new HashSet<DateTimeOffset>();

    /// <param name="definition">The reminder whose schedule columns + lead offsets drive the math.</param>
    /// <param name="from">The lower bound (inclusive) — typically <c>DateTimeOffset.UtcNow</c>.</param>
    /// <param name="cronEvaluator">Cron engine (from the scheduling contract) for <see cref="ReminderScheduleType.RecurringCron"/>.</param>
    /// <param name="alreadyDispatched">Occurrence instants already recorded in the dispatch log; skipped. Defaults to none.</param>
    public static DateTimeOffset? ComputeNextOccurrence(
        ReminderDefinition definition,
        DateTimeOffset from,
        ICronEvaluator cronEvaluator,
        IReadOnlySet<DateTimeOffset>? alreadyDispatched = null)
    {
        alreadyDispatched ??= NoneDispatched;
        return definition.ScheduleType switch
        {
            ReminderScheduleType.OneShot => NextOneShot(definition, from, alreadyDispatched),
            ReminderScheduleType.RecurringInterval => NextInterval(definition, from, alreadyDispatched),
            ReminderScheduleType.RecurringCron => NextCron(definition, from, cronEvaluator, alreadyDispatched),
            _ => null
        };
    }

    private static DateTimeOffset? NextOneShot(
        ReminderDefinition def, DateTimeOffset from, IReadOnlySet<DateTimeOffset> dispatched)
    {
        if (def.DueAt is not { } due)
            return null;

        return def.LeadOffsets
            .Select(o => due.AddMinutes(o.OffsetMinutes))
            .Where(occ => occ >= from && !dispatched.Contains(occ))
            .OrderBy(occ => occ)
            .Select(occ => (DateTimeOffset?)occ)
            .FirstOrDefault();
    }

    private static DateTimeOffset? NextInterval(
        ReminderDefinition def, DateTimeOffset from, IReadOnlySet<DateTimeOffset> dispatched)
    {
        if (def.IntervalPreset is not { } preset || def.AnchorDate is not { } anchor)
            return null;

        for (int n = StartIndex(anchor, preset, from), probed = 0; probed < MaxProbe; n++, probed++)
        {
            var occ = Occurrence(anchor, preset, n);
            if (def.EndsAt is { } ends && occ > ends)
                return null;
            if (occ >= from && !dispatched.Contains(occ))
                return occ;
        }

        return null;
    }

    private static DateTimeOffset? NextCron(
        ReminderDefinition def, DateTimeOffset from, ICronEvaluator cronEvaluator, IReadOnlySet<DateTimeOffset> dispatched)
    {
        if (string.IsNullOrWhiteSpace(def.Cron))
            return null;

        // GetNextOccurrence is strictly-after; subtract a tick so an occurrence exactly at `from` is included.
        var cursor = from.AddTicks(-1);
        for (var probed = 0; probed < MaxProbe; probed++)
        {
            if (cronEvaluator.GetNextOccurrence(def.Cron, cursor) is not { } occ)
                return null;
            if (def.EndsAt is { } ends && occ > ends)
                return null;
            if (!dispatched.Contains(occ))
                return occ;
            cursor = occ;
        }

        return null;
    }

    private static DateTimeOffset Occurrence(DateTimeOffset anchor, ReminderIntervalPreset preset, int n)
    {
        return preset switch
        {
            ReminderIntervalPreset.Daily => anchor.AddDays(n),
            ReminderIntervalPreset.Weekly => anchor.AddDays(7L * n),
            ReminderIntervalPreset.Monthly => anchor.AddMonths(n),
            ReminderIntervalPreset.Quarterly => anchor.AddMonths(3 * n),
            ReminderIntervalPreset.Yearly => anchor.AddYears(n),
            _ => anchor.AddDays(n)
        };
    }

    /// <summary>
    /// A lower-bound index <c>n</c> (never overshooting the first occurrence at/after <paramref name="from"/>)
    /// so the probe loop starts close instead of from the anchor; the loop advances and corrects exactly.
    /// </summary>
    private static int StartIndex(DateTimeOffset anchor, ReminderIntervalPreset preset, DateTimeOffset from)
    {
        if (from <= anchor)
            return 0;

        var span = from - anchor;
        var estimate = preset switch
        {
            ReminderIntervalPreset.Daily => (long)Math.Floor(span.TotalDays),
            ReminderIntervalPreset.Weekly => (long)Math.Floor(span.TotalDays / 7),
            ReminderIntervalPreset.Monthly => MonthsApart(anchor, from),
            ReminderIntervalPreset.Quarterly => MonthsApart(anchor, from) / 3,
            ReminderIntervalPreset.Yearly => from.Year - anchor.Year - 1,
            _ => 0L
        };
        return (int)Math.Max(0, estimate);
    }

    private static long MonthsApart(DateTimeOffset anchor, DateTimeOffset from) => (from.Year - anchor.Year) * 12L + (from.Month - anchor.Month) - 1;
}