using AdhdTimeOrganizer.Planning.application.dto.request.taskPlanner;
using AdhdTimeOrganizer.Planning.application.validator;
using AdhdTimeOrganizer.Planning.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.Planning.domain.serviceContract;
using Sydowwe.Framework.application.endpoint.@base.command;
using Sydowwe.Framework.application.extensions;

namespace AdhdTimeOrganizer.Planning.application.endpoint.activityPlanning.plannerTask.command;

/// <summary>
/// No reminder wiring here on purpose, and that is the opt-in guarantee working: a task that has just been
/// created cannot have a reminder yet — nothing can reference an id that did not exist a moment ago — and a
/// task never grows one on its own. The user attaches one afterwards by POSTing a <c>Reminder</c> with this
/// task's id, which is the only path that creates one.
/// <para>
/// It accepts a <c>Date</c> in place of a <c>CalendarId</c>, and that is the lazy-creation path: the calendar
/// row is made on the spot if the user has none for that day. Without it, a date past the seeded horizon
/// could not be planned at all — there is no create-calendar endpoint, so nothing else in the app can bring
/// that row into existence.
/// </para>
/// </summary>
public class CreatePlannerTaskEndpoint(DbContext dbContext, ICalendarProvisioner calendars)
    : BaseCreateEndpoint<PlannerTask, PlannerTaskRequest>(dbContext)
{
    private long? _resolvedCalendarId;

    public override void Configure()
    {
        base.Configure();
        Validator<PlannerTaskValidator>();
    }

    /// <summary>
    /// Resolved before mapping rather than after, because provisioning commits: doing it once this endpoint's
    /// own task is staged would drag that half-built write into the calendar's transaction.
    /// </summary>
    protected override async Task<bool> BeforeMapping(PlannerTaskRequest req, CancellationToken ct = default)
    {
        if (req.CalendarId is null && req.Date is { } date)
            _resolvedCalendarId = (await calendars.EnsureForDateAsync(User.GetId(), date, ct)).Id;

        return true;
    }

    protected override Task<bool> AfterMapping(PlannerTask entity, PlannerTaskRequest req, CancellationToken ct = default)
    {
        // ToEntity leaves this at 0 when the caller named a date — a DTO property cannot reach the database.
        if (_resolvedCalendarId is { } calendarId)
            entity.CalendarId = calendarId;

        return Task.FromResult(true);
    }
}
