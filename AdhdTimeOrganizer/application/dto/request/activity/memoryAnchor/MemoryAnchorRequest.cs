using System.ComponentModel.DataAnnotations;
using AdhdTimeOrganizer.application.dto.request.activity;

namespace AdhdTimeOrganizer.application.dto.request.activity.memoryAnchor;

public record MemoryAnchorRequest : ActivityIdRequest
{
    [Required] public int AnchorMonth { get; init; }
    [Required] public int AnchorYear { get; init; }
    [Required] public string HighlightNote { get; init; } = null!;
    [Required] public int Rating { get; init; }
}
