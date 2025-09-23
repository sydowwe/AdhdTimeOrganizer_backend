namespace AdhdTimeOrganizer.application.dto.request.@base;

public class ChangeDisplayOrderRequest
{
    // The item the user moved
    public long MovedItemId { get; set; }

    // The item it was dropped after (null if moved to top)
    public long? PrecedingItemId { get; set; }

    // The item it was dropped before (null if moved to bottom)
    public long? FollowingItemId { get; set; }
}