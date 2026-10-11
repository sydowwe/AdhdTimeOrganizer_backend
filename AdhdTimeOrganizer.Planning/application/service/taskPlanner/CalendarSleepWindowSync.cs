using AdhdTimeOrganizer.Core.domain.model.entity.user;
using AdhdTimeOrganizer.Planning.application.helper;
using AdhdTimeOrganizer.Planning.domain.model.entity;
using AdhdTimeOrganizer.Planning.domain.serviceContract;
using Sydowwe.Framework.config.dependencyInjection;

namespace AdhdTimeOrganizer.Planning.application.service.taskPlanner;

/// <inheritdoc cref="ICalendarSleepWindowSync"/>
public sealed class CalendarSleepWindowSync(DbContext dbContext, TimeProvider timeProvider, ILogger<CalendarSleepWindowSync> logger)
    : ICalendarSleepWindowSync, IScopedService
{
    public async Task<int> SyncFutureDaysAsync(User user, CancellationToken ct)
    {
        var today = TaskPlannerHelper.TodayIn(user.Timezone, timeProvider.GetUtcNow().UtcDateTime);
        var (wakeUpTime, bedTime) = user.SleepWindow;

        // Scoped by hand with the filters lifted, like CalendarProvisioner: the answer must not depend on who is signed
        // in. Rows already on the new window are skipped so the count means "days that changed".
        var changed = await dbContext.Set<Calendar>()
            .IgnoreQueryFilters()
            .Where(c => c.UserId == user.Id
                        && c.Date >= today
                        && !c.HasCustomSleepWindow
                        && (c.WakeUpTime != wakeUpTime || c.BedTime != bedTime))
            .ExecuteUpdateAsync(s => s
                .SetProperty(c => c.WakeUpTime, wakeUpTime)
                .SetProperty(c => c.BedTime, bedTime), ct);

        if (changed > 0)
            logger.LogInformation("Moved {DayCount} future days of user {UserId} onto their new sleep window", changed, user.Id);

        return changed;
    }
}
