using AdhdTimeOrganizer.Planning.application.dto.request.taskPlanner;
using AdhdTimeOrganizer.Planning.application.dto.response.taskPlanner;
using AdhdTimeOrganizer.Planning.application.validator;
using AdhdTimeOrganizer.Planning.domain.model.entity.activityPlanning;
using FastEndpoints;
using Sydowwe.Framework.application.dto.dto;
using Sydowwe.Framework.domain.helper;

namespace AdhdTimeOrganizer.Planning.application.endpoint.activityPlanning.calendar;

/// <summary>
/// <c>POST /calendar/task-summaries</c> — every planner task in a date range, grouped by day, in the reduced
/// shape a month cell draws. The batched counterpart of <c>planner-task/filter</c>, which takes one calendar
/// id and so had to be fired once per visible day: up to 31 requests per month, re-fired on every navigation.
/// <para>
/// It does not replace that filter and does not change it. The day view still wants the whole
/// <see cref="PlannerTaskResponse"/> for one day and a time window; the month grid wants five fields for
/// thirty-one days, which is a different question and gets its own answer
/// (<see cref="PlannerTaskSummaryResponse"/>).
/// </para>
/// <para>
/// <b>Days with no tasks are absent from the response</b>, not present-and-empty. Completion counts come off
/// <c>calendar/filter</c>, so the caller already knows which days have tasks before this lands; an empty entry
/// would carry no information a missing one doesn't.
/// </para>
/// </summary>
public class GetTaskSummariesCalendarEndpoint(DbContext dbContext)
    : Endpoint<CalendarTaskSummariesRequest, CalendarTaskSummariesResponse>
{
    protected virtual string[] AllowedRoles() => this.GetDefaultRoles();

    public override void Configure()
    {
        Post("/calendar/task-summaries");
        Roles(AllowedRoles());
        Validator<CalendarTaskSummariesValidator>();
        Summary(s =>
        {
            s.Summary = "Per-day planner task summaries for a date range";
            s.Description =
                "Returns every planner task falling in the given inclusive date range, grouped by calendar id "
                + "and ordered by start time, carrying only what a calendar cell renders: span, lane, colour "
                + "and activity name. Days with no tasks are omitted. Same range the caller sends to "
                + "calendar/filter.";
            s.Response<CalendarTaskSummariesResponse>(200, "Success");
            s.Response(400, "Bad request — Until before From, or a range over a year");
        });
    }

    public override async Task HandleAsync(CalendarTaskSummariesRequest req, CancellationToken ct)
    {
        // PlannerTask and Calendar are both IEntityWithUser, so the host's global query filter scopes this to
        // the signed-in user through both the set and the Calendar navigation — there is no user predicate to
        // write here, and adding one by hand would be the only copy that could fall out of step.
        var rows = await dbContext.Set<PlannerTask>()
            .AsNoTracking()
            .Where(t => t.Calendar.Date >= req.From && t.Calendar.Date <= req.Until)
            // Ordered before the projection: StartTime becomes a TimeDto in the response and no longer sorts
            // in the database. Date orders the groups; the caller keys by id but reads them in order.
            .OrderBy(t => t.Calendar.Date)
            .ThenBy(t => t.StartTime)
            .ThenBy(t => t.EndTime)
            .Select(t => new
            {
                t.CalendarId,
                Task = new PlannerTaskSummaryResponse
                {
                    Id = t.Id,
                    StartTime = new TimeDto(t.StartTime.Hour, t.StartTime.Minute),
                    EndTime = new TimeDto(t.EndTime.Hour, t.EndTime.Minute),
                    IsBackground = t.IsBackground,
                    Color = t.Activity.Role.Color,
                    ActivityName = t.Activity.Name
                }
            })
            .ToListAsync(ct);

        var days = rows
            .GroupBy(r => r.CalendarId)
            .Select(g => new CalendarDayTaskSummaryResponse
            {
                CalendarId = g.Key,
                Tasks = g.Select(r => r.Task).ToList()
            })
            .ToList();

        await Send.OkAsync(new CalendarTaskSummariesResponse { Days = days }, ct);
    }
}
