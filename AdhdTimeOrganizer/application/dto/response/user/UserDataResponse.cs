using AdhdTimeOrganizer.domain.model.@enum;

namespace AdhdTimeOrganizer.application.dto.response.user;

public record UserDataResponse
{
    public required long Id { get; init; }
    public required string Email { get; init; }
    public bool TwoFactorEnabled { get; init; }
    public required DateTime CreatedAt { get; init; }
    public DateTime? LastLoginAt { get; init; }
    public AppThemeEnum Theme { get; init; }
    public AvailableLocales Locale { get; init; }
    public required string Timezone { get; init; }
    public bool AskBeforeDelete { get; init; }
}
