using AdhdTimeOrganizer.Planning.application.dto.response.taskPlanner;
using AdhdTimeOrganizer.Planning.application.helper;
using AdhdTimeOrganizer.Planning.domain.model.entity;
using AdhdTimeOrganizer.Planning.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.Planning.domain.serviceContract;
using FastEndpoints;
using Sydowwe.Framework.application.extensions;
using Sydowwe.Framework.domain.helper;

namespace AdhdTimeOrganizer.Planning.application.endpoint.activityPlanning.calendar;

/// <summary>
/// <c>GET /calendar/day-plan/{date}</c> — the whole of one day in one round trip: the calendar row, its
/// planner tasks and the streak. Replaces the <c>by-Date</c> → <c>planner-task/filter</c> pair that the home
/// page had to run serially, because the filter is keyed on a calendar id only the first call could supply.
/// <para>
/// It hangs off the calendar rather than the planner task because the calendar is what a date resolves to;
/// the task list is what hangs off the calendar. Nothing about the old pair changes — the day-planner view
/// still filters by calendar id and time window, which is the shape it actually needs.
/// </para>
/// <para>
/// <b>No 404.</b> See <see cref="DayPlanResponse"/> for why absence is modelled as a successful empty result.
/// </para>
/// <para>
/// <b>A GET that writes (FO6).</b> For the user's today and tomorrow, auto-placing repeating rules are
/// materialised onto the plan first (<see cref="IRepeatingTaskMaterialiser"/>), which can create the calendar
/// row and the occurrences. Precedent: <c>GET /planner/settings</c> creates its row on first read. The write never
/// fails the read, and once a day is decided it costs one indexed probe.
/// </para>
/// </summary>
public class GetDayPlanCalendarEndpoint(
    DbContext dbContext,
    IPlannerStreakReader streakReader,
    IRepeatingTaskMaterialiser materialiser)
    : EndpointWithoutRequest<DayPlanResponse>
{
    protected virtual string[] AllowedRoles() => this.GetDefaultRoles();

    public override void Configure()
    {
        Get("/calendar/day-plan/{date}");
        Roles(AllowedRoles());
        Summary(s =>
        {
            s.Summary = "Get a day's whole plan";
            s.Description =
                "Returns the calendar row for the given date, every planner task on it ordered by start time, "
                + "and the user's completion streak. A date with no calendar row is a 200 with a null calendar "
                + "and no tasks — not a 404. Date is yyyy-MM-dd (dd-MM-yyyy also accepted). For the "
                + "user's today and tomorrow, auto-placing repeating tasks are put on the plan first, which may "
                + "create the calendar row and the occurrences.";
            s.Response<DayPlanResponse>(200, "Success — including for an unplanned day");
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        // The formats are shared with the FO4 routes hanging off this one (TaskPlannerHelper).
        if (!TaskPlannerHelper.TryParseDayPlanRouteDate(Route<string>("date"), out var date))
        {
            AddError("date", "Date must be in yyyy-MM-dd format.");
            await Send.ErrorsAsync(cancellation: ct);
            return;
        }

        // Before the reads below, since it commits and its occurrences are part of the day being returned.
        await materialiser.PlaceForReadAsync(User.GetId(), date, ct);

        // Both sets are IEntityWithUser, so the host's global query filter scopes them to the signed-in user;
        // the calendar id below therefore cannot be another user's, and the task read cannot reach across.
        var calendar = await CalendarResponse
            .Projection(dbContext.Set<Calendar>().AsNoTracking().Where(c => c.Date == date))
            .FirstOrDefaultAsync(ct);

        var tasks = calendar is null
            ? new List<PlannerTaskResponse>()
            // Ordered before the projection: StartTime becomes a TimeDto in the response and no longer sorts
            // in the database.
            : await PlannerTaskResponse
                .Projection(dbContext.Set<PlannerTask>()
                    .AsNoTracking()
                    .Where(t => t.CalendarId == calendar.Id)
                    .OrderBy(t => t.StartTime)
                    .ThenBy(t => t.EndTime))
                .ToListAsync(ct);

        var streak = await streakReader.GetForUserAsync(User.GetId(), ct);

        await Send.OkAsync(new DayPlanResponse
        {
            Date = date,
            // The streak rides on CalendarResponse for the by-Date route's sake; here it has its own field, so
            // null it out rather than shipping the same object twice under two names.
            Calendar = calendar is null ? null : calendar with { Streak = null },
            Tasks = tasks,
            Streak = streak
        }, ct);
    }
}
