namespace MojaDigitalnaFirma.Kernel.scheduling;

/// <summary>
/// The unit of an interval schedule, combined with an interval count → "every N units".
/// Only meaningful when <see cref="JobScheduleType.Interval"/> is used.
/// </summary>
public enum JobIntervalPreset
{
    Minute,
    Hour,
    Day,
    Week,
    Month,
    Quarter,
    Year
}