using AdhdTimeOrganizer.Core.domain.model.entity.user;
using AdhdTimeOrganizer.Core.domain.serviceContract;
using AdhdTimeOrganizer.Planning.application.dto.request.taskPlanner;
using AdhdTimeOrganizer.Planning.application.dto.response.taskPlanner;
using AdhdTimeOrganizer.Planning.application.helper;
using AdhdTimeOrganizer.Planning.application.validator;
using AdhdTimeOrganizer.Planning.domain.service;
using AdhdTimeOrganizer.Planning.domain.serviceContract;
using FastEndpoints;
using Sydowwe.Framework.application.dto.dto;
using Sydowwe.Framework.application.extensions;
using Sydowwe.Framework.domain.helper;

namespace AdhdTimeOrganizer.Planning.application.endpoint.activityPlanning.calendar;

/// <summary>
/// <c>GET /calendar/day-plan/{date}/next-free-slot?minutes=</c> — FO4's placement rule for a single block: the
/// earliest stretch of the day long enough for it, for home's <i>Move later</i>, leisure's "do it now" and the to-do
/// handoff. Same arithmetic as the re-plan (<see cref="DayPacking.FirstFreeSlot"/>), with nothing moving: every
/// block on the day that is not background or skipped holds its time — except <c>excludeTaskId</c>, the block being
/// moved.
/// <para>
/// <b>Window:</b> on the user's today it starts at their now (rounded up to the minute); on a later day at that day's
/// wake-up time (the user's default for a day with no row yet, FO10's <c>UserSleepWindow</c>). <c>notBeforeMinute</c> can only push
/// it later. It ends at bed time, or 23:59 when bed time is 00:00 or after midnight. A past day is a 400.
/// </para>
/// <para>
/// A GET that may write, like <c>GET /calendar/day-plan/{date}</c>: for today and tomorrow FO6's materialiser places
/// auto-placing repeating blocks first, so a slot is never offered over one about to appear.
/// </para>
/// </summary>
public class GetNextFreeSlotCalendarEndpoint(
    DbContext dbContext,
    IUserTimeZoneResolver timeZones,
    IRepeatingTaskMaterialiser materialiser,
    TimeProvider timeProvider)
    : Endpoint<NextFreeSlotRequest, NextFreeSlotResponse>
{
    protected virtual string[] AllowedRoles() => this.GetDefaultRoles();

    public override void Configure()
    {
        Get("/calendar/day-plan/{date}/next-free-slot");
        Roles(AllowedRoles());
        Validator<NextFreeSlotValidator>();
        Summary(s =>
        {
            s.Summary = "The earliest free slot of a given length on a day";
            s.Description =
                "The first stretch of `minutes` minutes not held by a foreground, non-skipped block, from now (today) or "
                + "the day's wake-up time (a later day) — or from notBeforeMinute if later — to bed time. excludeTaskId "
                + "leaves the block being moved out. No room is a 200 with found: false. Date is yyyy-MM-dd "
                + "(dd-MM-yyyy also accepted); a past date is a 400.";
            s.Response<NextFreeSlotResponse>(200, "The slot, or found: false");
            s.Response(400, "Malformed date or query, or a past date");
        });
    }

    public override async Task HandleAsync(NextFreeSlotRequest req, CancellationToken ct)
    {
        if (!TaskPlannerHelper.TryParseDayPlanRouteDate(Route<string>("date"), out var date))
        {
            AddError("date", "Date must be in yyyy-MM-dd format.");
            await Send.ErrorsAsync(cancellation: ct);
            return;
        }

        var userId = User.GetId();
        var zone = await timeZones.GetAsync(userId, ct);
        var utcNow = timeProvider.GetUtcNow().UtcDateTime;
        var today = TaskPlannerHelper.TodayIn(zone, utcNow);
        if (date < today)
        {
            AddError("date", "A past day has no free time left.");
            await Send.ErrorsAsync(cancellation: ct);
            return;
        }

        await materialiser.PlaceForReadAsync(userId, date, ct);

        var (calendar, tasks) = await DayReplanPlanner.LoadDayAsync(dbContext, userId, date, track: false, ct);
        var (wakeUpTime, bedTime) = calendar is not null
            ? new UserSleepWindow(calendar.WakeUpTime, calendar.BedTime)
            : await UserSleepWindow.ForUserAsync(dbContext, userId, ct);

        var earliest = date == today ? DayReplanPlanner.PackedFromNow(utcNow, zone) : DayPacking.MinuteOf(wakeUpTime);
        if (req.NotBeforeMinute is { } notBefore)
            earliest = Math.Max(earliest, notBefore);

        var occupied = tasks
            .Where(t => t.Id != req.ExcludeTaskId)
            .Select(DayReplanPlanner.Holds)
            .Where(held => held.HasValue)
            .Select(held => held!.Value)
            .ToList();

        var slot = DayPacking.FirstFreeSlot(occupied, req.Minutes, earliest, DayPacking.WindowEnd(wakeUpTime, bedTime));

        await Send.OkAsync(new NextFreeSlotResponse
        {
            Date = date,
            Minutes = req.Minutes,
            Found = slot.HasValue,
            StartTime = slot.HasValue ? ToDto(DayPacking.TimeOf(slot.Value.Start)) : null,
            EndTime = slot.HasValue ? ToDto(DayPacking.TimeOf(slot.Value.End)) : null
        }, ct);
    }

    private static TimeDto ToDto(TimeOnly time) => new(time.Hour, time.Minute);
}
