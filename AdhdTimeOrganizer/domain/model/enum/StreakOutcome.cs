namespace AdhdTimeOrganizer.domain.model.@enum;

/// <summary>
/// What a period reset did to the routine's streak — the fact <c>RoutineResetService.TryReset</c> establishes
/// as it evaluates completion against <c>StreakThreshold</c>.
/// <para>
/// Deliberately a portal domain enum rather than the Kernel's <c>RoutineStreakOutcome</c>, which this maps onto
/// in <c>RoutinePeriodNotificationService</c>. They carry the same members today, but the Kernel one is a
/// persisted notification-payload contract (old rows deserialize against it), while this one is free to follow
/// the streak rules wherever they go.
/// </para>
/// </summary>
public enum StreakOutcome
{
    /// <summary>Completion met the period's threshold — streak incremented.</summary>
    Extended,

    /// <summary>Threshold missed, but the period allows grace days, so the streak is held pending expiry.</summary>
    OnGrace,

    /// <summary>Threshold missed with no grace configured — streak reset to zero.</summary>
    Broken,

    /// <summary>
    /// The period held no items, so completion says nothing about the routine. <c>TryReset</c> leaves the
    /// streak untouched in this case, and the summary notification says nothing about it either.
    /// </summary>
    NotEvaluated
}
