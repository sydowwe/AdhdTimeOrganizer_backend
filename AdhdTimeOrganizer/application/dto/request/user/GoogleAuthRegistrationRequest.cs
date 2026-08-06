using AdhdTimeOrganizer.domain.model.entity.user;
using Sydowwe.Framework.application.dto.request.user;

namespace AdhdTimeOrganizer.application.dto.request.user;

public record GoogleAuthRegistrationRequest : RegistrationRequest
{
    public required string GoogleOAuthUserId { get; init; }

    public User ToEntity => PopulateBaseFields(new User { Timezone = TimeZoneInfo.FindSystemTimeZoneById(Timezone) });
}