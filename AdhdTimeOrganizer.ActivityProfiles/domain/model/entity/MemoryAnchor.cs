using AdhdTimeOrganizer.Core.domain.model.entity.activity;

namespace AdhdTimeOrganizer.ActivityProfiles.domain.model.entity;

public class MemoryAnchor : BaseEntityWithActivity
{
    public int AnchorMonth { get; set; }
    public int AnchorYear { get; set; }
    public string HighlightNote { get; set; } = null!;
    public int Rating { get; set; }
}