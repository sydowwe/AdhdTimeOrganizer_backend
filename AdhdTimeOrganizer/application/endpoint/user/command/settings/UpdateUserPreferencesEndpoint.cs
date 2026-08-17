using AdhdTimeOrganizer.application.dto.request.user;
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

        // Null keeps whatever is stored (the base's convention for every field); an empty or whitespace-only
        // string is the client clearing the setting, and is stored as null so "not set" has one representation.
        if (req.WeatherLocation is not null)
            user.WeatherLocation = string.IsNullOrWhiteSpace(req.WeatherLocation) ? null : req.WeatherLocation.Trim();
    }
}