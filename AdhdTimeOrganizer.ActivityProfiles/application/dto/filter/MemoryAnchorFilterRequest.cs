using Sydowwe.Framework.application.dto.request.@interface;

namespace AdhdTimeOrganizer.ActivityProfiles.application.dto.filter;

/// <inheritdoc cref="ActivityBacklogProfileFilterRequest" path="/summary"/>
public record MemoryAnchorFilterRequest : IFilterRequest
{
    /// <summary>Case-insensitive contains on the activity's name.</summary>
    public string? ActivityName { get; set; }

    /// <summary>Inclusive: keeps anchors rated at least this.</summary>
    public int? MinRating { get; set; }

    /// <summary>
    /// The period, under the entity's names. The client sent <c>year</c> / <c>month</c> until F60 renamed
    /// them to these — the server side of that agreement is to keep them, not to alias the old pair.
    /// </summary>
    public int? AnchorMonth { get; set; }

    /// <inheritdoc cref="AnchorMonth"/>
    public int? AnchorYear { get; set; }

    public long? ActivityId { get; set; }
}
