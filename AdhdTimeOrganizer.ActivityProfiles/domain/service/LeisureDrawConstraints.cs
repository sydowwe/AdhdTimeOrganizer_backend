using AdhdTimeOrganizer.ActivityProfiles.domain.model.@enum;

namespace AdhdTimeOrganizer.ActivityProfiles.domain.service;

/// <summary>
/// What the user said they have available. The two lookup ids are resolved to row sets before the query
/// runs, so they are not part of the rule itself — the rule only sees time, energy and party size.
/// </summary>
/// <param name="Minutes">Minutes available right now. The one constraint that gates almost everything.</param>
/// <param name="Energy">Energy right now.</param>
/// <param name="People">People available, including the user.</param>
public readonly record struct LeisureDrawConstraints(int Minutes, EnergyLevel Energy, int People);

/// <summary>
/// Everything the ranking needs beyond the candidates themselves.
/// </summary>
/// <param name="Constraints">What the user has available.</param>
/// <param name="LastSuggestedAt">
/// Candidate key → UTC instant it was last put in front of the user. Read from
/// <c>LeisureSuggestionRecord</c>, which is why rerolling on a phone now moves the laptop's draw too.
/// </param>
/// <param name="LastCommittedEffort">
/// The effort type of the last suggestion the user actually committed to. Drives the variety bonus; null
/// when they have never committed to one, or when the one they did records no effort type.
/// </param>
/// <param name="Now">Reference instant for staleness. Injected so the rule is testable.</param>
/// <param name="Seed">
/// The draw. Same seed + same pool + same history ⇒ the same cards in the same order, which is what makes
/// a reloaded <c>?seed=</c> URL show the user the three things they were deciding between.
/// </param>
public sealed record LeisureRankingContext(
    LeisureDrawConstraints Constraints,
    IReadOnlyDictionary<string, DateTime> LastSuggestedAt,
    EffortType? LastCommittedEffort,
    DateTime Now,
    uint Seed);
