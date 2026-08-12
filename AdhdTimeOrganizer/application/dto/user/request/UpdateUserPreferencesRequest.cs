using Sydowwe.Framework.application.dto.request.user;

namespace AdhdTimeOrganizer.application.dto.request.user;

/// <summary>
/// Theme / Locale / Timezone / AskBeforeDelete come from <see cref="UserPreferencesRequest"/>;
/// <see cref="FirstDayOfWeek"/> is this portal's own <c>User</c> column.
/// </summary>
public record UpdateUserPreferencesRequest : UserPreferencesRequest
{
    public int? FirstDayOfWeek { get; init; }
}