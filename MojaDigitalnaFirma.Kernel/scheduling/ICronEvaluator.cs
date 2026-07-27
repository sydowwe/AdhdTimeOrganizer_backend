namespace MojaDigitalnaFirma.Kernel.scheduling;

/// <summary>
/// A stateless cron expression evaluator: validate an expression and compute its next fire time. Lives on
/// the scheduling contract so the cron engine (Quartz) stays owned by the Scheduler module
/// (Core.Scheduler) and consumers depend only on this interface — the same seam as <see cref="IScheduler"/>.
/// <para>
/// This is <b>expression math only</b>; it neither schedules nor runs anything. The Reminders module uses
/// it to compute a recurring-cron reminder's per-occurrence <c>NextOccurrenceAt</c> in its own DB, without
/// referencing Core.Scheduler or taking a Quartz dependency itself.
/// </para>
/// </summary>
public interface ICronEvaluator
{
    /// <summary>True if <paramref name="cron"/> is a syntactically valid (Quartz-syntax) cron expression.</summary>
    bool IsValid(string cron);

    /// <summary>
    /// The next fire time strictly after <paramref name="afterUtc"/>, interpreted in UTC, or <c>null</c> if
    /// the expression has no further fire times. To make a boundary inclusive (fire <i>at or after</i> an
    /// instant), pass that instant minus one tick. Throws if <paramref name="cron"/> is invalid — validate
    /// with <see cref="IsValid"/> first.
    /// </summary>
    DateTimeOffset? GetNextOccurrence(string cron, DateTimeOffset afterUtc);
}