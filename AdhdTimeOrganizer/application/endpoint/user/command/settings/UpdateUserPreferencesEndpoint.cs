using AdhdTimeOrganizer.application.dto.request.user;
using AdhdTimeOrganizer.application.validator;
using AdhdTimeOrganizer.Core.domain.model.entity.user;
using AdhdTimeOrganizer.Planning.domain.serviceContract;
using AdhdTimeOrganizer.TodoLists.domain.serviceContract;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.application.endpoint.user.command.settings;

namespace AdhdTimeOrganizer.application.endpoint.user.command.settings;

public class UpdateUserPreferencesEndpoint(
    UserManager<User> userManager,
    DbContext dbContext,
    ITodoListItemDueReminderSync dueReminders,
    ICalendarSleepWindowSync sleepWindowSync)
    : BaseUpdateUserPreferencesEndpoint<User, UpdateUserPreferencesRequest>(userManager, dbContext)
{
    /// <summary>
    /// A to-do's due instant is its date and time read in the user's zone, and the registered reminder froze the
    /// old zone's instant. Re-sync every open timed item when the zone is sent, or the dispatch guard — which
    /// compares instants — would read every one of them as moved and retire it.
    /// <para>
    /// FO10: a sent sleep window moves the user's untouched days onto it. A sent zone does too, since it moves which
    /// day is "today" and so where the rewrite starts; the sync is idempotent, so running it with nothing changed
    /// costs one update that matches no row.
    /// </para>
    /// </summary>
    protected override async Task AfterUpdateAsync(User user, UpdateUserPreferencesRequest req, CancellationToken ct)
    {
        if (req.Timezone is not null)
            await dueReminders.SyncAllForUserAsync(user.Id, ct);

        if (req.DefaultWakeUpTime is not null || req.DefaultBedTime is not null || req.Timezone is not null)
            await sleepWindowSync.SyncFutureDaysAsync(user, ct);
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

        if (req.MorningBriefingEnabled.HasValue)
            user.MorningBriefingEnabled = req.MorningBriefingEnabled.Value;

        if (req.MorningBriefingOffsetMinutes.HasValue)
            user.MorningBriefingOffsetMinutes = req.MorningBriefingOffsetMinutes.Value;

        // Null keeps whatever is stored (the base's convention for every field); an empty or whitespace-only
        // string is the client clearing the setting, and is stored as null so "not set" has one representation.
        if (req.WeatherLocation is not null)
            user.WeatherLocation = string.IsNullOrWhiteSpace(req.WeatherLocation) ? null : req.WeatherLocation.Trim();

        if (req.DefaultWakeUpTime is not null)
            user.DefaultWakeUpTime = req.DefaultWakeUpTime.ToTimeOnly();
        if (req.DefaultBedTime is not null)
            user.DefaultBedTime = req.DefaultBedTime.ToTimeOnly();
    }
}
