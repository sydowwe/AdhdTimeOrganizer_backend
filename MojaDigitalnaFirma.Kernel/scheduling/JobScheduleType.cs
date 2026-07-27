namespace MojaDigitalnaFirma.Kernel.scheduling;

/// <summary>Discriminates how a job's schedule is expressed (see <see cref="ScheduleSpec"/>).</summary>
public enum JobScheduleType
{
    Interval,
    Cron,

    /// <summary>A one-shot: fire exactly once at a fixed instant (<see cref="ScheduleSpec.RunAtUtc"/>), no recurrence.</summary>
    Once
}