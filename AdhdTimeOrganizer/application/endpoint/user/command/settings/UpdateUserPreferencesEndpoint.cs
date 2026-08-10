using AdhdTimeOrganizer.application.dto.request.user;
using AdhdTimeOrganizer.Core.application.validator;
using AdhdTimeOrganizer.application.validator;
using AdhdTimeOrganizer.Core.domain.model.entity.user;
using Microsoft.AspNetCore.Identity;
using Sydowwe.Framework.application.endpoint.user.command.settings;

namespace AdhdTimeOrganizer.application.endpoint.user.command.settings;

public class UpdateUserPreferencesEndpoint(UserManager<User> userManager)
    : BaseUpdateUserPreferencesEndpoint<User, UpdateUserPreferencesRequest>(userManager)
{
    public override void Configure()
    {
        base.Configure();
        Validator<UpdateUserPreferencesValidator>();
    }

    protected override void ApplyExtraPreferences(User user, UpdateUserPreferencesRequest req)
    {
        if (req.FirstDayOfWeek.HasValue)
            user.FirstDayOfWeek = req.FirstDayOfWeek.Value;
    }
}