using AdhdTimeOrganizer.application.dto.response.@base;
using AdhdTimeOrganizer.domain.model.entity.activity.memoryAnchor;


namespace AdhdTimeOrganizer.application.dto.response.activity.memoryAnchor;

public record MemoryAnchorResponse : IdResponse, IProjectionResponse<MemoryAnchorResponse, MemoryAnchor>
{
    public required long ActivityId { get; init; }
    public required int AnchorMonth { get; init; }
    public required int AnchorYear { get; init; }
    public required string HighlightNote { get; init; }
    public required int Rating { get; init; }

    public static IQueryable<MemoryAnchorResponse> Projection(IQueryable<MemoryAnchor> query) =>
        query.Select(e => new MemoryAnchorResponse
        {
            Id = e.Id,
            ActivityId = e.ActivityId,
            AnchorMonth = e.AnchorMonth,
            AnchorYear = e.AnchorYear,
            HighlightNote = e.HighlightNote,
            Rating = e.Rating,
        });

    public static MemoryAnchorResponse FromEntity(MemoryAnchor e) =>
        new()
        {
            Id = e.Id,
            ActivityId = e.ActivityId,
            AnchorMonth = e.AnchorMonth,
            AnchorYear = e.AnchorYear,
            HighlightNote = e.HighlightNote,
            Rating = e.Rating,
        };
}
