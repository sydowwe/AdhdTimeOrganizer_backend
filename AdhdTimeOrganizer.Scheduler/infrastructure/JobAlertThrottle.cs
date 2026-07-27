using Sydowwe.Framework.config.dependencyInjection;

namespace AdhdTimeOrganizer.Scheduler.infrastructure;

/// <summary>
/// Noise control for failure alerting: at most <b>one alert per <c>JobKey</c> per window</b>. Without it, a
/// frequently-scheduled job in a persistent failure state alerts on <i>every</i> fire — the reminders scan
/// runs every 5 minutes by default, so a broken dependency would page every Admin hundreds of times a day
/// (email included), and the alerts stop being read. One alert per hour per job carries the same
/// information.
/// <para>
/// <b>Only the notification is throttled — never the ledger.</b> Every failure is still written to
/// <c>scheduled_job_run</c> and still logged; the run log remains the complete, authoritative record. A
/// suppressed alert is a suppressed *courtesy*, not a lost failure.
/// </para>
/// <para>
/// Singleton, in-process — the same single-node reasoning as <see cref="IJobConcurrencyGate"/>, and
/// deliberately consistent with it. Two consequences, both accepted: state resets on restart (so the first
/// failure after a restart always alerts — the safe direction), and throttling is per-process (correct on
/// single-node; multi-node would need a shared store, alongside everything else that would). The map is
/// keyed by job key and therefore bounded by the number of registered jobs — no eviction needed.
/// </para>
/// </summary>
public interface IJobAlertThrottle
{
    /// <summary>
    /// Whether an alert for <paramref name="alertKey"/> should be delivered now. Returns <c>true</c> at most
    /// once per window per key, and <b>records the alert as sent</b> when it returns <c>true</c> — so callers
    /// must only call it when they are actually about to alert.
    /// </summary>
    /// <param name="alertKey">
    /// The throttle bucket. The dispatcher passes the bare <c>JobKey</c>; the overdue sweep prefixes its kind
    /// (<c>"overdue:"</c>) so a job that is BOTH failing and overdue can't have one alert swallow the other —
    /// they are different problems with different fixes.
    /// </param>
    /// <param name="window">
    /// Minimum gap for this bucket; defaults to <see cref="JobAlertThrottle.Window"/>. The overdue sweep passes
    /// a longer one: a failing job re-alerts only as often as it re-fires, but an overdue job stays overdue
    /// <i>continuously</i> until someone fixes it, so an hourly repeat would page every Admin round the clock
    /// (email included) for a condition they already know about.
    /// </param>
    bool ShouldAlert(string alertKey, DateTime nowUtc, TimeSpan? window = null);
}

public sealed class JobAlertThrottle : IJobAlertThrottle, ISingletonService
{
    /// <summary>
    /// Default minimum gap between two alerts for the same bucket. Owner policy, not statutory — an hour is
    /// short enough that a newly-broken job is noticed promptly, long enough that a job failing every few
    /// minutes can't flood a mailbox. Reviewed and kept in follow-up 06 (D1).
    /// </summary>
    public static readonly TimeSpan Window = TimeSpan.FromHours(1);

    private readonly Dictionary<string, DateTime> _lastAlertUtc = [];
    private readonly Lock _sync = new();

    // A plain lock (not a ConcurrentDictionary) because this must atomically decide AND record in one step:
    // ConcurrentDictionary's update factories may run more than once under contention, which would let two
    // concurrent failures of the same job both observe "allowed". Contention here is negligible — the path
    // runs only on a terminal job failure.
    public bool ShouldAlert(string alertKey, DateTime nowUtc, TimeSpan? window = null)
    {
        var gap = window ?? Window;
        lock (_sync)
        {
            if (_lastAlertUtc.TryGetValue(alertKey, out var last) && nowUtc - last < gap)
                return false;

            _lastAlertUtc[alertKey] = nowUtc;
            return true;
        }
    }
}