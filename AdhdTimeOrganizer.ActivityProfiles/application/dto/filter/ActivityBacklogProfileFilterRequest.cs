using AdhdTimeOrganizer.ActivityProfiles.domain.model.@enum;
using Sydowwe.Framework.application.dto.request.@interface;

namespace AdhdTimeOrganizer.ActivityProfiles.application.dto.filter;

public record ActivityBacklogProfileFilterRequest : IFilterRequest
{
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