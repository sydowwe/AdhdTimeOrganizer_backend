namespace AdhdTimeOrganizer.Planning.application.dto.@enum;

/// <summary>
/// What <c>POST /calendar/day-plan/{date}/replan/apply</c> does with a block the re-plan had no room for (FO4).
/// Every such block is resolved inside the action — none is parked as a list for later.
/// <para>On the wire as the member name: <c>"Tomorrow"</c> / <c>"Skip"</c>.</para>
/// </summary>
public enum ReplanWontFitResolution
{
    /// <summary>
    /// Moved to the next day at the same time of day, creating that day if needed. A move like any other, so it is
    /// recorded as a postponement when the block's original slot has passed — never as a carry-over.
    /// </summary>
    Tomorrow,

    /// <summary>Skipped (<c>Cancelled</c>) with the built-in reason <c>SkipReasonKey.NoTime</c>.</summary>
    Skip
}
