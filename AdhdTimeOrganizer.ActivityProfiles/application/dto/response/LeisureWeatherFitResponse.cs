namespace AdhdTimeOrganizer.ActivityProfiles.application.dto.response;

/// <summary>
/// Which of the caller's own <c>activity-weather-dependency</c> rows today's weather fits.
///
/// <para><b>Ids, not a condition.</b> The lookup rows are user-editable — renameable, translatable, deletable —
/// so nothing on the wire could tie one of them to a fixed condition word without breaking the first time a user
/// renamed a row. Resolving the set here reduces the client to
/// <c>matchingWeatherDependencyIds.includes(row.id)</c>, which cannot drift.</para>
/// </summary>
public record LeisureWeatherFitResponse
{
    /// <summary>
    /// Always includes the "none / any weather" row when the user has one: it fits every day by definition.
    /// Empty means no signal — no location set, or the provider had nothing — and the client treats that
    /// identically to the call having failed.
    /// </summary>
    public required IReadOnlyList<long> MatchingWeatherDependencyIds { get; init; }
}
