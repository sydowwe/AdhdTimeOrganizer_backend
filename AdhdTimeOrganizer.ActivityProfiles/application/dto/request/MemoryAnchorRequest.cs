using AdhdTimeOrganizer.ActivityProfiles.domain.model.entity;
using AdhdTimeOrganizer.Core.application.dto.request.activity;
using Sydowwe.Framework.application.dto.request.@interface;

namespace AdhdTimeOrganizer.ActivityProfiles.application.dto.request;

public record MemoryAnchorRequest : ActivityIdRequest, IMyRequest<MemoryAnchor>
{
    public int AnchorMonth { get; init; }
    public int AnchorYear { get; init; }
    public string HighlightNote { get; init; } = null!;
    public int Rating { get; init; }

    public MemoryAnchor ToEntity => new() { ActivityId = ActivityId, AnchorMonth = AnchorMonth, AnchorYear = AnchorYear, HighlightNote = HighlightNote, Rating = Rating };

    /// <summary>
    /// <c>ActivityId</c> is deliberately not written here: which activity an anchor is about is identity,
    /// not an editable field, and <c>CreateMemoryAnchorValidator</c> is the only place the
    /// two rules that gate an anchor onto an activity ("it must have a backlog or bucket-list profile" and
    /// "at most 2 per activity per month") are checked. Rewriting the column on update walked past both.
    /// The three profile request DTOs leave their <c>ActivityId</c> alone for the same reason.
    /// </summary>
    public void UpdateEntity(MemoryAnchor e)
    {
        e.AnchorMonth = AnchorMonth;
        e.AnchorYear = AnchorYear;
        e.HighlightNote = HighlightNote;
        e.Rating = Rating;
    }
}
