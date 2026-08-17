using AdhdTimeOrganizer.ActivityProfiles.domain.model.@enum;
using Sydowwe.Framework.application.dto.request.@interface;

namespace AdhdTimeOrganizer.ActivityProfiles.application.dto.filter;

public record ActivityBacklogProfileFilterRequest : IFilterRequest
{
    public EnergyLevel? EnergyLevel { get; set; }
    public EffortType? EffortType { get; set; }
    public bool? IsRepeatable { get; set; }

    /// <summary>
    /// The inverse of <see cref="IsRepeatable"/>, under the name the clients use for it. Both are accepted
    /// and both are applied, so sending the two in contradiction ("repeatable and one-time") legitimately
    /// matches nothing.
    /// </summary>
    public bool? IsOneTime { get; set; }

    /// <summary>
    /// Tri-state, like <see cref="IsRepeatable"/>: null does not filter, true keeps only entries whose
    /// activity has a MemoryAnchor, false keeps the rest. Matches the response field, so a repeatable entry
    /// is never anchored and always falls on the false side. Also serves the "n of m experienced" readout,
    /// which asks for a page of 1 twice — with <c>isOneTime: true</c> both times — and reads only
    /// <c>itemsCount</c>.
    /// </summary>
    public bool? IsAnchored { get; set; }
}