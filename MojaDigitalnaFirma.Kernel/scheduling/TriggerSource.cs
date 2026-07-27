namespace MojaDigitalnaFirma.Kernel.scheduling;

/// <summary>How an execution was initiated — recorded on every run for provenance.</summary>
public enum TriggerSource
{
    /// <summary>Fired by its recurring schedule.</summary>
    Scheduled,

    /// <summary>Fired once on demand (operator "trigger now"), off-schedule.</summary>
    Manual,

    /// <summary>Re-invoked from a past run's captured payload.</summary>
    Replay,

    /// <summary>
    /// An automatic off-schedule re-fire after a <see cref="Scheduled"/> (or a prior <see cref="Retry"/>)
    /// fire failed — see the dispatcher's auto-retry-with-backoff. Like <see cref="Manual"/>/<see cref="Replay"/>
    /// it carries a <c>null</c> scheduled fire time, so it is never matched by the scheduled-occurrence dedup.
    /// Unlike them it is <b>retry-eligible</b>: a failed <see cref="Retry"/> spawns the next attempt (up to the
    /// job's max), whereas a failed <see cref="Manual"/>/<see cref="Replay"/> never retries (an operator is watching).
    /// </summary>
    Retry
}