using AdhdTimeOrganizer.Core.domain.model.entity.activity.memoryAnchor;
using Sydowwe.Framework.application.dto.request.@interface;

namespace AdhdTimeOrganizer.Core.application.dto.request.activity.memoryAnchor;

public record MemoryAnchorRequest : ActivityIdRequest, IMyRequest<MemoryAnchor>
{
    public int AnchorMonth { get; init; }
    public int AnchorYear { get; init; }
    public string HighlightNote { get; init; } = null!;
    public int Rating { get; init; }

    public MemoryAnchor ToEntity => new() { ActivityId = ActivityId, AnchorMonth = AnchorMonth, AnchorYear = AnchorYear, HighlightNote = HighlightNote, Rating = Rating };

    public void UpdateEntity(MemoryAnchor e)
    {
        e.ActivityId = ActivityId;
        e.AnchorMonth = AnchorMonth;
        e.AnchorYear = AnchorYear;
        e.HighlightNote = HighlightNote;
        e.Rating = Rating;
    }
}