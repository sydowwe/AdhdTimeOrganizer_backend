using AdhdTimeOrganizer.application.dto.response.@base;

namespace AdhdTimeOrganizer.application.dto.response.activity.memoryAnchor;

public record MemoryAnchorResponse : IdResponse
{
    public required long ActivityId { get; init; }
    public required int AnchorMonth { get; init; }
    public required int AnchorYear { get; init; }
    public required string HighlightNote { get; init; }
    public required int Rating { get; init; }
}
