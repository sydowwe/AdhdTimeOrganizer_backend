using AdhdTimeOrganizer.application.dto.request.@interface;
using AdhdTimeOrganizer.domain.model.@enum;

namespace AdhdTimeOrganizer.application.dto.request.user;

public record CreateUserRequest : ICreateRequest
{
    public required string Email { get; set; }

    public string? Password { get; set; }

    public required AvailableLocales CurrentLocale { get; set; }
    public required TimeZoneInfo Timezone { get; set; }
}