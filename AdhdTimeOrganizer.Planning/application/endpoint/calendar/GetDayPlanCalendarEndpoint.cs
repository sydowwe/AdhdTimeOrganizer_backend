using AdhdTimeOrganizer.Planning.application.dto.response.taskPlanner;
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
/// </summary>
public class GetDayPlanCalendarEndpoint(DbContext dbContext, IPlannerStreakReader streakReader)
    : EndpointWithoutRequest<DayPlanResponse>
{
    /// <summary>
    /// The date formats accepted on the route. ISO first because that is what a client holding a local date
    /// already has; <c>dd-MM-yyyy</c> second only so this route cannot be the one that breaks when someone
    /// copies a URL over from the sibling <c>by-Date</c> route, which speaks that format alone.
    /// </summary>
    private static readonly string[] DateFormats = ["yyyy-MM-dd", "dd-MM-yyyy"];

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
                + "and no tasks — not a 404. Date is yyyy-MM-dd (dd-MM-yyyy also accepted).";
            s.Response<DayPlanResponse>(200, "Success — including for an unplanned day");
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var raw = Route<string>("date");
        if (!DateOnly.TryParseExact(raw, DateFormats, out var date))
        {
            AddError("date", "Date must be in yyyy-MM-dd format.");
            await Send.ErrorsAsync(cancellation: ct);
            return;
        }

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
