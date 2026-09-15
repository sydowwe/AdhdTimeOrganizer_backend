using AdhdTimeOrganizer.ActivityProfiles.domain.model.@enum;
using Sydowwe.Framework.application.dto.request.@interface;

namespace AdhdTimeOrganizer.ActivityProfiles.application.dto.filter;

/// <summary>
/// The member names here are the contract with the leisure grids' filter chips and URL codecs.
/// System.Text.Json drops unknown members silently, so a misnamed member is not an error — it is an
/// unfiltered grid with a 200. <c>GridFilterFieldNameTests</c> pins every name by asserting a narrowed
/// count; add a case there when adding a member here.
/// </summary>
public record ActivityBacklogProfileFilterRequest : IFilterRequest
{
    /// <summary>Case-insensitive contains on the activity's name.</summary>
    public string? ActivityName { get; set; }

    /// <summary>Any-of over the lookup ids. Null or empty means "no opinion".</summary>
    public long[]? LocationTypeIds { get; set; }

    /// <inheritdoc cref="LocationTypeIds"/>
    public long[]? WeatherDependencyIds { get; set; }

    /// <inheritdoc cref="LocationTypeIds"/>
    public long[]? ExpectedCostTierIds { get; set; }

    /// <summary>Any-of. The client's chips; <see cref="EnergyLevel"/> is the older exact-match scalar.</summary>
    public EnergyLevel[]? EnergyLevels { get; set; }

    /// <inheritdoc cref="EnergyLevels"/>
    public EffortType[]? EffortTypes { get; set; }

    /// <summary>Inclusive: keeps entries whose <c>DurationMinutes</c> is at most this.</summary>
    public int? MaxDurationMinutes { get; set; }

    /// <summary>Inclusive: keeps entries whose <c>MinParticipants</c> is at least this.</summary>
    public int? MinParticipants { get; set; }

    /// <summary>
    /// Inclusive: keeps entries whose <c>MaxParticipants</c> is at most this. An entry with no upper bound
    /// (<c>MaxParticipants</c> null) is "any number", so it never satisfies a cap and is dropped.
    /// </summary>
    public int? MaxParticipants { get; set; }

    public EnergyLevel? EnergyLevel { get; set; }
    public EffortType? EffortType { get; set; }
    /// <summary>
    /// Tri-state: null does not filter, true keeps repeatable entries, false keeps the rest.
    /// </summary>
    /// <remarks>
    /// There is deliberately <b>no <c>isOneTime</c> alias</b>. It existed briefly, accepted alongside this
    /// and applied as its inverse, which meant the same question had two spellings on one request and a
    /// caller could send them in contradiction. One name in one direction, and a client asking "is it
    /// one-time" sends <c>isRepeatable: false</c>.
    /// </remarks>
    public bool? IsRepeatable { get; set; }

    /// <summary>
    /// Tri-state, like <see cref="IsRepeatable"/>: null does not filter, true keeps only entries whose
    /// activity has a MemoryAnchor, false keeps the rest. Matches the response field, so a repeatable entry
    /// is never anchored and always falls on the false side. Also serves the "n of m experienced" readout,
    /// which asks for a page of 1 twice — with <c>isRepeatable: false</c> both times — and reads only
    /// <c>itemsCount</c>.
    /// </summary>
    public bool? IsAnchored { get; set; }
}
