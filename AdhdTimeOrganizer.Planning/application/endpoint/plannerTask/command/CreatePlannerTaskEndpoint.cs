using AdhdTimeOrganizer.Core.domain.serviceContract;
using AdhdTimeOrganizer.Planning.application.dto.request.taskPlanner;
using AdhdTimeOrganizer.Planning.application.endpoint.extensions;
using AdhdTimeOrganizer.Planning.application.helper;
using AdhdTimeOrganizer.Planning.application.validator;
using AdhdTimeOrganizer.Planning.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.Planning.domain.serviceContract;
using Sydowwe.Framework.application.endpoint.@base.command;
using Sydowwe.Framework.application.extensions;

namespace AdhdTimeOrganizer.Planning.application.endpoint.activityPlanning.plannerTask.command;

/// <summary>
/// The one reminder wiring here is audit N1's: once the task has saved, <see cref="IPlannedBlockAutoReminders"/>
/// gives it an automatic reminder if it qualifies under the user's <c>AutoReminderMode</c>. Any other reminder is
/// still the user's to attach afterwards, by POSTing a <c>Reminder</c> with this task's id.
/// <para>
/// It accepts a <c>Date</c> in place of a <c>CalendarId</c>, and that is the lazy-creation path: the calendar
/// row is made on the spot if the user has none for that day. Without it, a date past the seeded horizon
/// could not be planned at all — there is no create-calendar endpoint, so nothing else in the app can bring
/// that row into existence.
/// </para>
/// </summary>
public class CreatePlannerTaskEndpoint(
    DbContext dbContext,
    ICalendarProvisioner calendars,
    IReferencedIdOwnershipService ownership,
    IPlannedBlockAutoReminders autoReminders)
    : BaseCreateEndpoint<PlannerTask, PlannerTaskRequest>(dbContext)
{
    private readonly DbContext _dbContext = dbContext;

    private long? _resolvedCalendarId;

    public override void Configure()
    {
        base.Configure();
        Validator<PlannerTaskValidator>();
    }

    /// <summary>
    /// Resolved before mapping rather than after, because provisioning commits: doing it once this endpoint's
    /// own task is staged would drag that half-built write into the calendar's transaction.
    /// <para>
    /// The ownership check runs before provisioning for the same reason it commits: a request naming a
    /// stranger's activity must not leave a calendar row behind on its way to a 404.
    /// </para>
    /// </summary>
    protected override async Task<bool> BeforeMapping(PlannerTaskRequest req, CancellationToken ct = default)
    {
        if (!await this.EnsureOwnsPlannerTaskReferencesAsync(_dbContext, ownership, req, ct))
            return false;

        if (req.CalendarId is null && req.Date is { } date)
            _resolvedCalendarId = (await calendars.EnsureForDateAsync(User.GetId(), date, ct)).Id;

        return true;
    }

    /// <summary>Lands the resolved day, then refuses a third overlapping block (<see cref="PlannerTaskLaneRule"/>).</summary>
    protected override Task<bool> AfterMapping(PlannerTask entity, PlannerTaskRequest req, CancellationToken ct = default)
    {
        // ToEntity leaves this at 0 when the caller named a date — a DTO property cannot reach the database.
        if (_resolvedCalendarId is { } calendarId)
            entity.CalendarId = calendarId;

        return this.EnsureWithinLanesAsync(_dbContext, entity, ct);
    }

    protected override Task AfterSave(PlannerTask entity, CancellationToken ct = default) =>
        autoReminders.CreateForNewBlocksAsync([entity.Id], ct);
}
