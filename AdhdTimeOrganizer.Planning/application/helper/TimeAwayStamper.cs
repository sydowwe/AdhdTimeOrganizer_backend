using AdhdTimeOrganizer.Core.domain.model.entity.user;
using AdhdTimeOrganizer.Core.domain.model.@enum;
using AdhdTimeOrganizer.Planning.domain.model.entity;
using AdhdTimeOrganizer.Planning.domain.service;

namespace AdhdTimeOrganizer.Planning.application.helper;

/// <summary>
/// F4-02: what a <see cref="TimeAway"/> does to its days, in one place, behind its create, its resize and its
/// delete. In the <c>PlannerTemplateApplier</c> style: it only <b>stages</b> on the passed <c>DbContext</c> and
/// never saves, so the caller's one <c>SaveChanges</c> commits the stretch and its stamp together — a stretch whose
/// days are half-stamped is a state no screen can explain.
/// <para>
/// <b>The stamp</b>, per day of the range: the day joins the stretch (<c>Calendar.TimeAwayId</c>); its type
/// becomes the stretch's <see cref="TimeAway.Kind"/> when it is natural (<c>Workday</c> / <c>Weekend</c>) or
/// already that kind, and stays as it is otherwise — a <c>Special</c> day, or a sick day inside a vacation, is the
/// user's own override; and the stretch's <see cref="TimeAway.Location"/> fills a day whose location is unset.
/// Weekends are stamped too: a Saturday in Barcelona is away, and <c>CalendarDayTypeRule</c> allows both away
/// types on any day.
/// </para>
/// <para>
/// <b>The un-stamp</b> is keyed on the link plus equality with what the stamp wrote: a linked day whose type is
/// still the kind goes back to its natural type (<see cref="CalendarDayFactory.NaturalDayType"/>), and its location
/// is cleared if it still equals the stretch's. Nothing else is touched, and never a task. One consequence is stated
/// rather than hidden: a day that already carried the kind (or the location) before the stretch arrived is
/// indistinguishable from one the stamp wrote, so it reverts with the rest. The design accepted that rather than
/// recording a per-day "before".
/// </para>
/// <para>
/// Missing days are created here in bulk through <see cref="CalendarDayFactory"/>, the single definition of a new
/// day, and not through <c>ICalendarProvisioner.EnsureForDateAsync</c>, which commits once per date. Every read
/// scopes by hand on the stretch's own <c>UserId</c>, as the provisioner does.
/// </para>
/// </summary>
public static class TimeAwayStamper
{
    /// <summary>
    /// Stamps <paramref name="stretch"/> onto its range — after un-stamping the days it held before, when
    /// <paramref name="previous"/> (what the stamp wrote last time) is given, which is how a resize, a rename or a
    /// change of kind is applied. <paramref name="stretch"/> must carry its <b>new</b> values and be tracked; on a
    /// create it may still be unsaved, and the days take its id through the navigation.
    /// </summary>
    public static async Task<TimeAwayStaging> StageAsync(
        DbContext dbContext, TimeAway stretch, TimeAwayStamp? previous, CancellationToken ct)
    {
        var held = previous is null ? [] : await LoadHeldAsync(dbContext, stretch, ct);
        var range = await ProvisionAsync(dbContext, stretch.UserId, stretch.StartDate, stretch.EndDate, ct);

        // Snapshot before anything moves, over both sets, so a day that leaves and a day that stays are judged the
        // same way: the event wants the days whose type differs at the end, not every day that was touched — a day
        // re-stamped with the kind it already had did not move.
        var before = held.Concat(range).DistinctBy(d => d.Date).ToDictionary(d => d.Date, d => d.DayType);

        if (previous is { } written)
        {
            foreach (var day in held)
                Unstamp(day, written);
        }

        var days = range.Select(day => new TimeAwayStagedDay(day, Stamp(day, stretch))).ToList();
        var released = held.Where(d => d.Date < stretch.StartDate || d.Date > stretch.EndDate).Select(d => d.Date).Order().ToList();

        return new TimeAwayStaging(days, released, ChangedDates(before, held.Concat(range)));
    }

    /// <summary>
    /// Un-stamps every day <paramref name="stretch"/> holds, ahead of its delete. Returns the dates whose type
    /// moved, for <c>CalendarDayTypesChangedEvent</c>.
    /// </summary>
    public static async Task<IReadOnlyList<DateOnly>> StageRemovalAsync(DbContext dbContext, TimeAway stretch, CancellationToken ct)
    {
        var held = await LoadHeldAsync(dbContext, stretch, ct);
        var before = held.ToDictionary(d => d.Date, d => d.DayType);

        var written = TimeAwayStamp.Of(stretch);
        foreach (var day in held)
            Unstamp(day, written);

        return ChangedDates(before, held);
    }

    /// <returns>Whether the day took the stretch's kind (<c>false</c>: it kept a type of the user's own).</returns>
    private static bool Stamp(Calendar day, TimeAway stretch)
    {
        day.TimeAway = stretch;
        // The id as well as the navigation once there is one: a day re-stamped by a resize was just un-stamped, and
        // a null FK beside a set navigation leaves EF to pick a winner. A new stretch has no id yet (EF keeps its
        // temporary key off the instance), and the navigation alone fills the column on the save.
        if (stretch.Id != 0)
            day.TimeAwayId = stretch.Id;

        var takesKind = day.DayType is DayType.Workday or DayType.Weekend || day.DayType == stretch.Kind;
        if (takesKind)
            day.DayType = stretch.Kind;

        day.Location ??= stretch.Location;
        return takesKind;
    }

    private static void Unstamp(Calendar day, TimeAwayStamp written)
    {
        day.TimeAway = null;
        day.TimeAwayId = null;

        if (day.DayType == written.Kind)
            day.DayType = CalendarDayFactory.NaturalDayType(day.Date);

        if (written.Location is { } location && day.Location == location)
            day.Location = null;
    }

    /// <summary>The days linked to <paramref name="stretch"/> now, tracked. Empty for one not yet saved.</summary>
    private static async Task<List<Calendar>> LoadHeldAsync(DbContext dbContext, TimeAway stretch, CancellationToken ct) =>
        stretch.Id == 0
            ? []
            : await dbContext.Set<Calendar>()
                .IgnoreQueryFilters()
                .Where(c => c.UserId == stretch.UserId && c.TimeAwayId == stretch.Id)
                .ToListAsync(ct);

    /// <summary>
    /// Every day of <c>[start, end]</c>, tracked and in date order, adding the missing ones (staged, not saved).
    /// Scoped by hand rather than through the global filter, for <c>CalendarProvisioner</c>'s reason: filtered, a
    /// caller acting for another user would read no row and insert duplicates onto <c>(user_id, date)</c>.
    /// </summary>
    private static async Task<List<Calendar>> ProvisionAsync(
        DbContext dbContext, long userId, DateOnly start, DateOnly end, CancellationToken ct)
    {
        var existing = await dbContext.Set<Calendar>()
            .IgnoreQueryFilters()
            .Where(c => c.UserId == userId && c.Date >= start && c.Date <= end)
            .ToDictionaryAsync(c => c.Date, ct);

        UserSleepWindow? sleepWindow = null;
        var days = new List<Calendar>(end.DayNumber - start.DayNumber + 1);

        for (var date = start; date <= end; date = date.AddDays(1))
        {
            if (!existing.TryGetValue(date, out var day))
            {
                sleepWindow ??= await UserSleepWindow.ForUserAsync(dbContext, userId, ct);
                day = CalendarDayFactory.Create(userId, date, sleepWindow.Value);
                dbContext.Set<Calendar>().Add(day);
            }

            days.Add(day);
        }

        return days;
    }

    private static List<DateOnly> ChangedDates(IReadOnlyDictionary<DateOnly, DayType> before, IEnumerable<Calendar> days) =>
        days.DistinctBy(d => d.Date)
            .Where(d => before[d.Date] != d.DayType)
            .Select(d => d.Date)
            .Order()
            .ToList();
}

/// <summary>
/// What a stamp wrote onto its days, which is what the un-stamp compares against. Captured <b>before</b> an update
/// maps the request onto the stretch — afterwards the entity holds the new kind and location, and un-stamping by
/// those would leave the old ones behind.
/// </summary>
public readonly record struct TimeAwayStamp(DayType Kind, Location? Location)
{
    public static TimeAwayStamp Of(TimeAway stretch) => new(stretch.Kind, stretch.Location);
}

/// <param name="Day">One day of the range, tracked; a new one has no id until the save.</param>
/// <param name="TookKind">Whether its type became the stretch's kind, or it kept one of the user's own.</param>
public sealed record TimeAwayStagedDay(Calendar Day, bool TookKind);

/// <param name="Days">Every day of the stretch's range, in date order.</param>
/// <param name="ReleasedDates">Days the stretch held before and no longer covers (a shrink or a move).</param>
/// <param name="DayTypeChangedDates">Days whose type differs once staged — the event's dates.</param>
public sealed record TimeAwayStaging(
    IReadOnlyList<TimeAwayStagedDay> Days,
    IReadOnlyList<DateOnly> ReleasedDates,
    IReadOnlyList<DateOnly> DayTypeChangedDates);
