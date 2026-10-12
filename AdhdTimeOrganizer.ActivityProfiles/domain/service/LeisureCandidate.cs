using AdhdTimeOrganizer.ActivityProfiles.domain.model;
using AdhdTimeOrganizer.ActivityProfiles.domain.model.@enum;

namespace AdhdTimeOrganizer.ActivityProfiles.domain.service;

/// <summary>
/// Everything the draw reads about one candidate, flattened out of whichever of the three profiles it
/// came from. The ranking sees only this — never an entity — so the rule stays testable without a
/// database and cannot start depending on a column the response does not carry.
/// </summary>
/// <param name="Source">Which table this came from.</param>
/// <param name="ActivityId">The activity the profile hangs off. With <paramref name="Source"/>, the identity of the candidate.</param>
/// <param name="StatedDurationMinutes">
/// The duration the source actually records, and <c>null</c> when it records none. Backlog only — a
/// project's estimate covers the whole build rather than one sitting, and a bucket-list entry records no
/// duration at all.
/// </param>
/// <param name="MaxUsefulMinutes">
/// The longest this could usefully occupy: the backlog's stated duration, or a project's whole estimate.
/// Sizes the slot the user books; never excludes.
/// </param>
/// <param name="EnergyLevel">Stated by the backlog, derived by the other two.</param>
/// <param name="EnergyIsDerived">
/// Whether <paramref name="EnergyLevel"/> was stated or inferred. It travels to the card, which labels a
/// derived value as an estimate — so it has to be honest rather than convenient.
/// </param>
/// <param name="MinParticipants">Backlog only; <c>null</c> elsewhere, because nothing else records a party size.</param>
/// <param name="ReadinessStatus">Project only.</param>
/// <param name="ComfortZoneStep">Bucket list only, 1–5.</param>
/// <param name="RequiresTravel">Bucket list only; <c>false</c> elsewhere.</param>
/// <param name="MaxParticipants">
/// Backlog only, and optional there. Ranks rather than excludes: a two-player game is still something to do when
/// four people are around, it is just a worse answer than one for all four.
/// </param>
/// <param name="WeatherCode">
/// Backlog only: the <see cref="WeatherDependencyCodes"/> value the entry's weather-dependency row resolves to,
/// and only when it is a <b>real</b> dependency (<c>sunny</c> / <c>dry</c> / <c>snow</c>). A <c>none</c> row fits
/// every day, so letting it through would boost every indoor activity whenever a location is set; a row whose
/// label infers nothing is "no opinion". Both are <c>null</c> here, so weather neither excludes nor boosts them.
/// </param>
public sealed record LeisureCandidate(
    LeisureSuggestionSource Source,
    long ActivityId,
    int? StatedDurationMinutes,
    int? MaxUsefulMinutes,
    EnergyLevel EnergyLevel,
    bool EnergyIsDerived,
    EffortType? EffortType,
    int? MinParticipants,
    ReadinessStatus? ReadinessStatus,
    int? ComfortZoneStep,
    bool RequiresTravel,
    int? MaxParticipants = null,
    string? WeatherCode = null)
{
    /// <summary>
    /// Stable across draws, and what the jitter hashes — so it has to be the same string the client keys
    /// its cards on.
    /// </summary>
    public string Key { get; } = LeisureSuggestionKey.Format(Source, ActivityId);
}
