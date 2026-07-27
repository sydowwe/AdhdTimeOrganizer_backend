namespace MojaDigitalnaFirma.Kernel.scheduling;

/// <summary>
/// What to do when a fire is missed (process down, thread-pool starvation, etc.). Maps to Quartz
/// misfire instructions in the Scheduler module — declared here so owners can express intent without
/// referencing Quartz.
/// </summary>
public enum MisfirePolicy
{
    /// <summary>Fire once now to catch up, then resume the normal schedule.</summary>
    FireAndProceed,

    /// <summary>Skip the missed fire(s) and continue on schedule as if nothing was missed.</summary>
    IgnoreMisfires,

    /// <summary>Do not catch up; wait for the next scheduled fire.</summary>
    DoNothing
}