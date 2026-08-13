using AdhdTimeOrganizer.Planning.application.dto.request.taskPlanner;
using AdhdTimeOrganizer.Planning.application.validator;
using AdhdTimeOrganizer.Planning.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.Planning.domain.serviceContract;
using Sydowwe.Framework.application.endpoint.@base.command;
using Sydowwe.Framework.application.extensions;

namespace AdhdTimeOrganizer.Planning.application.endpoint.activityPlanning.plannerTask.command;

public class UpdatePlannerTaskEndpoint(
    DbContext dbContext,
    IReminderRegistrationService reminders,
    ICalendarProvisioner calendars)
    : BaseUpdateEndpoint<PlannerTask, PlannerTaskRequest>(dbContext)
{
    private long? _resolvedCalendarId;

    public override void Configure()
    {
        base.Configure();
        Validator<PlannerTaskValidator>();
    }

    /// <summary>
    /// Moving a task onto a day the user has never planned creates that day, exactly as creating a task there
    /// does — otherwise a task could be dragged onto a date the app has no row for and the update would fail
    /// on a foreign key. Resolved before the entity is fetched because provisioning commits.
    /// </summary>
    protected override async Task<bool> BeforeMapping(PlannerTaskRequest req, CancellationToken ct = default)
    {
        if (req.CalendarId is null && req.Date is { } date)
            _resolvedCalendarId = (await calendars.EnsureForDateAsync(User.GetId(), date, ct)).Id;

        return true;
    }

    protected override Task<bool> AfterMapping(PlannerTask entity, PlannerTaskRequest req, CancellationToken ct = default)
    {
        // UpdateEntity leaves CalendarId alone when the caller named a date instead; this is where that lands.
        if (_resolvedCalendarId is { } calendarId)
            entity.CalendarId = calendarId;

        return Task.FromResult(true);
    }

    /// <summary>
    /// A task-linked reminder's instant is derived from the task's day + start time, so any full update has to
    /// re-register — an update can move the time, move the day (a different calendar) or change the status.
    /// A task with no reminder attached resolves to an empty set and costs one indexed lookup.
    /// </summary>
    protected override Task AfterSave(PlannerTask entity, CancellationToken ct = default) => reminders.SyncForPlannerTasksAsync([entity.Id], ct);
}
