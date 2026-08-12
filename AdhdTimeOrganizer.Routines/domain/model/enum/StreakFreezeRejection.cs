namespace AdhdTimeOrganizer.Routines.domain.model.@enum;

/// <summary>
/// Why <c>RoutineStreakFreezeService.Apply</c> refused to spend a freeze — or <see cref="None"/> if it did.
/// <para>
/// The rules are domain rules, not request validation: whether a period counts as missed depends on its own
/// <c>StreakThreshold</c>, and whether a freeze is affordable depends on a budget window that refills lazily.
/// Neither is knowable from the request body, so neither belongs in the validator.
/// </para>
/// </summary>
public enum StreakFreezeRejection
{
    /// <summary>The freeze was spent; the period and the covered completion row were both mutated.</summary>
    None,

    /// <summary>The budget for the current window is exhausted.</summary>
    NoBudget,

    /// <summary>That period is already covered by a freeze — spending a second one would change nothing.</summary>
    AlreadyFrozen,

    /// <summary>That period met its threshold, so there is no miss to cover.</summary>
    NotAMiss
}
