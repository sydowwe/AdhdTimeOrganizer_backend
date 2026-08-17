using AdhdTimeOrganizer.ActivityProfiles.domain.model.@enum;
using AdhdTimeOrganizer.Core.domain.model.entity.activity;

namespace AdhdTimeOrganizer.ActivityProfiles.domain.model.entity;

/// <summary>
/// What the leisure picker has already put in front of this user, and how it went. One row per
/// (user, source, activity) — the draw's memory, and the reason the same three cards do not come back
/// every visit.
/// <para>
/// This used to live in the browser's <c>localStorage</c>, which made it per-device: rerolling on a
/// phone did not stop the laptop offering the same three. That is the half of the picker that cannot be
/// fixed client-side at all, so the record is a row here.
/// </para>
/// <para>
/// <b>The natural key is (source, activity), not the wire key string.</b> The API speaks
/// <c>"bucketList:8"</c> because that is what the card is keyed on, but storing the string would put a
/// parsed-on-every-read composite in a text column and lose the FK. <c>BaseEntityWithActivity</c> gives
/// the cascade for free: delete the activity and its draw history goes with it, so no row can outlive
/// the thing it remembers.
/// </para>
/// </summary>
public class LeisureSuggestionRecord : BaseEntityWithActivity
{
    /// <summary>
    /// Which profile the suggestion was drawn from. Part of the key rather than derived: one activity
    /// can carry a backlog profile *and* a project profile, and those are two different suggestions
    /// with two independent histories.
    /// </summary>
    public LeisureSuggestionSource Source { get; set; }

    /// <summary>UTC. When this candidate was last shown *and* acted on — see <see cref="LastOutcome"/>.</summary>
    public DateTime LastSuggestedAt { get; set; }

    public LeisureSuggestionOutcome LastOutcome { get; set; }
}
