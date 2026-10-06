using AdhdTimeOrganizer.Core.application.@event;
using AdhdTimeOrganizer.Planning.application.dto.request.taskPlanner;
using AdhdTimeOrganizer.Planning.application.dto.response.taskPlanner;
using AdhdTimeOrganizer.Planning.application.helper;
using AdhdTimeOrganizer.Planning.application.validator;
using AdhdTimeOrganizer.Planning.domain.model.entity;
using FastEndpoints;
using Sydowwe.Framework.domain.helper;

namespace AdhdTimeOrganizer.Planning.application.endpoint.activityPlanning.calendar;

/// <summary>
/// <c>PATCH /calendar/day-type/batch</c> — set the day type on several days at once. Replaces a fan-out of
/// <c>PUT /calendar/{id}</c>, one per selected day, each echoing back every other field of the day unchanged.
/// <para>
/// It writes <b>only</b> the day type. That is the whole of what the bulk action changed, and it is why this
/// is a PATCH rather than a batched PUT: the client no longer has to hold a current copy of every selected day
/// to change one field on it.
/// </para>
/// <para>
/// The weekday rule (<see cref="CalendarDayTypeRule"/>) is the same one <c>UpdateCalendarEndpoint</c> enforces,
/// but a violation is <b>that day's</b> <c>succeeded: false</c> rather than a 400 for the batch. Selecting a
/// week and marking it Workday is an ordinary thing to do, and the Saturday in it must not cancel the other
/// five days.
/// </para>
/// </summary>
public class BatchUpdateDayTypeCalendarEndpoint(DbContext dbContext)
    : Endpoint<BatchUpdateCalendarDayTypeRequest, CalendarBatchResultResponse>
{
    protected virtual string[] AllowedRoles() => this.GetDefaultRoles();

    public override void Configure()
    {
        Patch("/calendar/day-type/batch");
        Roles(AllowedRoles());
        Validator<BatchUpdateCalendarDayTypeValidator>();
        Summary(s =>
        {
            s.Summary = "Set the day type on several days";
            s.Description =
                "Sets DayType — and nothing else — on every named calendar day. Answers one result per "
                + "requested calendar id, in request order; a day the type is impossible on (Workday on a "
                + "weekend, Weekend on a weekday) fails on its own without failing the batch.";
            s.Response<CalendarBatchResultResponse>(200, "Applied — inspect results[] for per-day outcome");
            s.Response(400, "Bad request");
        });
    }

    public override async Task HandleAsync(BatchUpdateCalendarDayTypeRequest req, CancellationToken ct)
    {
        var results = new List<CalendarBatchResult>(req.CalendarIds.Count);
        // Days whose type actually moved, for the event below. Every day here belongs to the caller (the filter
        // scopes the lookup), so one event carries them all.
        var changedDates = new List<DateOnly>();
        long? userId = null;

        foreach (var calendarId in req.CalendarIds)
        {
            try
            {
                // Scoped by the global user filter: another user's id does not resolve, and reads as this
                // day's succeeded:false exactly like a stale one.
                var calendar = await dbContext.Set<Calendar>().FirstOrDefaultAsync(c => c.Id == calendarId, ct);
                if (calendar == null)
                {
                    results.Add(CalendarBatchResult.Failed(calendarId, CalendarBatchFailureReason.NotFound));
                    continue;
                }

                if (CalendarDayTypeRule.Violation(calendar, req.DayType) != null)
                {
                    results.Add(CalendarBatchResult.Failed(calendarId, CalendarBatchFailureReason.DayTypeNotAllowed));
                    continue;
                }

                var moved = calendar.DayType != req.DayType;
                calendar.DayType = req.DayType;
                await dbContext.SaveChangesAsync(ct);

                if (moved)
                {
                    changedDates.Add(calendar.Date);
                    userId = calendar.UserId;
                }

                results.Add(CalendarBatchResult.Ok(calendarId));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // The failed day's modified entity stays tracked and would be re-sent with every later day's
                // save, turning one failure into a whole failed batch.
                Logger.LogWarning(ex, "Setting the day type on calendar {CalendarId} failed; continuing with the rest of the batch", calendarId);

                dbContext.ChangeTracker.Clear();
                results.Add(CalendarBatchResult.Failed(calendarId, CalendarBatchFailureReason.Error));
            }
        }

        // Once for the batch, after every day committed: Routines excuses the closed cycles a newly marked
        // vacation or sick stretch covers, and a week marked in one go must be judged as a week, not day by day.
        // The handler logs and swallows, so it cannot turn the batch's 200 into a 500.
        if (userId is { } owner && changedDates.Count > 0)
            await new CalendarDayTypesChangedEvent(owner, changedDates).PublishAsync(Mode.WaitForAll, ct);

        await Send.OkAsync(new CalendarBatchResultResponse { Results = results }, ct);
    }
}
