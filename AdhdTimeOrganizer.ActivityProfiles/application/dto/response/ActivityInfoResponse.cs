namespace AdhdTimeOrganizer.ActivityProfiles.application.dto.response;

/// <summary>
/// The little of an <c>Activity</c> a card needs: what to call it and what to draw it as. Deliberately not
/// <c>ActivityResponse</c> — that one carries the whole role and category objects, and a suggestion card
/// renders a name, a subtitle and an avatar.
/// </summary>
/// <param name="Id">The activity id, and what a planner task is created against.</param>
/// <param name="Name">The activity name.</param>
/// <param name="CategoryName">Its category's name, when it has one — the card's subtitle.</param>
/// <param name="Icon">
/// The category's icon, falling back to the role's. Roles always carry both an icon and a colour and
/// categories are optional, so the fallback is what keeps a card from rendering a blank avatar for an
/// uncategorised activity.
/// </param>
/// <param name="Color">The category's colour, falling back to the role's.</param>
public record ActivityInfoResponse(long Id, string Name, string? CategoryName, string? Icon, string? Color);
