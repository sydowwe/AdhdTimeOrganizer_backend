using AdhdTimeOrganizer.application.dto.request.user;
using AdhdTimeOrganizer.application.validator;
using AdhdTimeOrganizer.Core.domain.model.entity.user;
using AdhdTimeOrganizer.TodoLists.domain.serviceContract;
using Microsoft.AspNetCore.Identity;
using Sydowwe.Framework.application.endpoint.user.command.settings;

namespace AdhdTimeOrganizer.application.endpoint.user.command.settings;

public class UpdateUserPreferencesEndpoint(UserManager<User> userManager, ITodoListItemDueReminderSync dueReminders)
    : BaseUpdateUserPreferencesEndpoint<User, UpdateUserPreferencesRequest>(userManager)
{
    /// <summary>
    /// A to-do's due instant is its date and time read in the user's zone, and the registered reminder froze the
    /// old zone's instant. Re-sync every open timed item when the zone is sent, or the dispatch guard — which
    /// compares instants — would read every one of them as moved and retire it.
    /// </summary>
    protected override async Task AfterUpdateAsync(User user, UpdateUserPreferencesRequest req, CancellationToken ct)
    {
        if (req.Timezone is not null)
            await dueReminders.SyncAllForUserAsync(user.Id, ct);
    }

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