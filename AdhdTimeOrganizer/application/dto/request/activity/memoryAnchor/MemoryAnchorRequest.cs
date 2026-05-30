using System.ComponentModel.DataAnnotations;
using AdhdTimeOrganizer.application.dto.request.@interface;
using AdhdTimeOrganizer.domain.model.entity.activity.memoryAnchor;

namespace AdhdTimeOrganizer.application.dto.request.activity.memoryAnchor;

public record MemoryAnchorRequest : ActivityIdRequest, IMyRequest<MemoryAnchor>
{
    [Required] public int AnchorMonth { get; init; }
    [Required] public int AnchorYear { get; init; }
    [Required] public string HighlightNote { get; init; } = null!;
    [Required] public int Rating { get; init; }

    public MemoryAnchor ToEntity => new() { UserId = 0, ActivityId = ActivityId, AnchorMonth = AnchorMonth, AnchorYear = AnchorYear, HighlightNote = HighlightNote, Rating = Rating };

    public void UpdateEntity(MemoryAnchor e)
    {
        e.ActivityId = ActivityId;
        e.AnchorMonth = AnchorMonth;
        e.AnchorYear = AnchorYear;
        e.HighlightNote = HighlightNote;
        e.Rating = Rating;
    }
}
