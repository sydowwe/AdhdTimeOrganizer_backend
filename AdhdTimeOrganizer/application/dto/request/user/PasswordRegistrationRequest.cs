using AdhdTimeOrganizer.domain.model.entity.user;
using Sydowwe.Framework.application.dto.request.user;

namespace AdhdTimeOrganizer.application.dto.request.user;

public record PasswordRegistrationRequest : BasePasswordRegistrationRequest<User>
{
    public override User ToEntity => PopulateBaseFields(new User { Timezone = TimeZoneInfo.FindSystemTimeZoneById(Timezone) });
}