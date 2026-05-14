using AdhdTimeOrganizer.domain.model.@enum;

namespace AdhdTimeOrganizer.application.dto.request.user;

public record UpdateUserPreferencesRequest
{
    public AppThemeEnum? Theme { get; init; }
    public AvailableLocales? Locale { get; init; }
    public string? Timezone { get; init; }
    public int? FirstDayOfWeek { get; init; }
    public bool? AskBeforeDelete { get; init; }
}
