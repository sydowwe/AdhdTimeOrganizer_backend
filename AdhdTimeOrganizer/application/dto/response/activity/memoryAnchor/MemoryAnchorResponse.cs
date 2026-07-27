using AdhdTimeOrganizer.domain.model.entity.activity.memoryAnchor;
using Sydowwe.Framework.application.dto.response;
using Sydowwe.Framework.application.dto.response.@base;

namespace AdhdTimeOrganizer.application.dto.response.activity.memoryAnchor;

public record MemoryAnchorResponse : IdResponse, IProjectionResponse<MemoryAnchorResponse, MemoryAnchor>
{
    public required long ActivityId { get; init; }
    public required int AnchorMonth { get; init; }
    public required int AnchorYear { get; init; }
    public required string HighlightNote { get; init; }
    public required int Rating { get; init; }

    public static IQueryable<MemoryAnchorResponse> Projection(IQueryable<MemoryAnchor> query)
    {
        return query.Select(e => new MemoryAnchorResponse
        {
            Id = e.Id,
            ActivityId = e.ActivityId,
            AnchorMonth = e.AnchorMonth,
            AnchorYear = e.AnchorYear,
            HighlightNote = e.HighlightNote,
            Rating = e.Rating
        });
    }
}