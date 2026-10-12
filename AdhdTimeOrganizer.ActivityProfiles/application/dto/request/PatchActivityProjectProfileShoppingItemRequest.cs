using Sydowwe.Framework.application.dto.request.@interface;

namespace AdhdTimeOrganizer.ActivityProfiles.application.dto.request;

/// <summary>
/// Links the to-do item the shopping handoff just created to its project (FO13), so ticking the item done
/// can make the project ready. Sent by the handoff dialog straight after it creates the item.
/// </summary>
public record PatchActivityProjectProfileShoppingItemRequest : IPatchRequest
{
    public required long TodoListItemId { get; init; }
}
