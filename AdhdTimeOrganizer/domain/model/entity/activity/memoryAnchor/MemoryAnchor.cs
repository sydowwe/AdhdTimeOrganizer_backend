namespace AdhdTimeOrganizer.domain.model.entity.activity.memoryAnchor;

public class MemoryAnchor : BaseEntityWithActivity
{
    public int AnchorMonth { get; set; }
    public int AnchorYear { get; set; }
    public string HighlightNote { get; set; } = null!;
    public int Rating { get; set; }
}
