using AdhdTimeOrganizer.Core.domain.model.entity.user;
using AdhdTimeOrganizer.Planning.domain.model.entity;
using AdhdTimeOrganizer.Planning.domain.service;
using Sydowwe.Framework.config.dependencyInjection;
using Sydowwe.Framework.Contracts.notification;
using Sydowwe.Framework.Contracts.user;

namespace AdhdTimeOrganizer.Planning.application.seam;

/// <summary>
/// P3-01: quiet hours that follow the sleep schedule, read off the <see cref="Calendar"/> rows — each day's own
/// <c>WakeUpTime</c> / <c>BedTime</c>, so a day the user moved is the night they get (<see cref="SleepNight"/>). A
/// date with no row uses the user's default window, which is what that row would be created with.
/// <para>
/// The Notifications module calls this only for users whose quiet-hours row follows the schedule, and from the
/// unauthenticated Reminders scan as well as from a request dispatching to someone else — so the reads are scoped
/// by the ids given with the query filters lifted, never by whoever is signed in.
/// </para>
/// </summary>
public sealed class CalendarSleepScheduleReader(DbContext dbContext, IUserTimeZoneReader userTimeZoneReader)
    : ISleepScheduleReader, IScopedService
{
    /// <summary>
    /// The default window as <c>[bed, wake)</c>: 23:00 / 07:00 is 1380 → 420 (overnight), the 00:00 / 08:00 fallback
    /// is 0 → 480. Bed equal to wake-up gives equal minutes — no usual night, as <see cref="SleepNight"/> reads it.
    /// </summary>
    public async Task<(int StartMinute, int EndMinute)> GetUsualNightAsync(long userId, CancellationToken ct = default)
    {
        var window = await UserSleepWindow.ForUserAsync(dbContext, userId, ct);
        return (MinuteOf(window.BedTime), MinuteOf(window.WakeUpTime));
    }

    public async Task<IReadOnlyDictionary<long, DateTimeOffset>> GetAsleepUntilAsync(
        IReadOnlyCollection<long> userIds, DateTimeOffset now, CancellationToken ct = default)
    {
        var asleepUntil = new Dictionary<long, DateTimeOffset>();

        var ids = userIds.Distinct().ToList();
        if (ids.Count == 0)
            return asleepUntil;

        var zones = await userTimeZoneReader.GetTimeZonesAsync(ids, ct);
        var zoneOf = ids.ToDictionary(id => id, id => zones.GetValueOrDefault(id, TimeZoneInfo.Utc));

        // Yesterday through tomorrow on each user's own clock covers both nights that can contain `now`. One range
        // for the whole batch: the users' todays differ by a day at most.
        var todays = zoneOf.Values.Select(z => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, z).DateTime)).ToList();
        var from = todays.Min().AddDays(-1);
        var to = todays.Max().AddDays(1);

        var days = await dbContext.Set<Calendar>()
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(c => ids.Contains(c.UserId) && c.Date >= from && c.Date <= to)
            .Select(c => new { c.UserId, c.Date, c.WakeUpTime, c.BedTime })
            .ToListAsync(ct);
        var byDay = days.ToDictionary(d => (d.UserId, d.Date), d => new UserSleepWindow(d.WakeUpTime, d.BedTime));

        // CalendarSeeder keeps two years of rows ahead, so the defaults are almost never needed; read them anyway, in
        // one query, rather than guess the fallback for a user whose rows are missing.
        var defaults = await dbContext.Set<User>()
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(u => ids.Contains(u.Id))
            .Select(u => new { u.Id, u.DefaultWakeUpTime, u.DefaultBedTime })
            .ToDictionaryAsync(u => u.Id, u => new UserSleepWindow(u.DefaultWakeUpTime, u.DefaultBedTime), ct);

        foreach (var id in ids)
        {
            var fallback = defaults.GetValueOrDefault(id, UserSleepWindow.Fallback);
            var wakeAt = SleepNight.WakeAt(now, zoneOf[id], date => byDay.GetValueOrDefault((id, date), fallback));

            if (wakeAt is { } end)
                asleepUntil[id] = end;
        }

        return asleepUntil;
    }

    private static int MinuteOf(TimeOnly time) => time.Hour * 60 + time.Minute;
}
