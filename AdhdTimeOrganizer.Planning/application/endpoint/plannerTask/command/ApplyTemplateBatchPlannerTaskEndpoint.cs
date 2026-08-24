using AdhdTimeOrganizer.Planning.application.dto.request.taskPlanner;
using AdhdTimeOrganizer.Planning.application.dto.response.taskPlanner;
using AdhdTimeOrganizer.Planning.application.helper;
using AdhdTimeOrganizer.Planning.application.validator;
using AdhdTimeOrganizer.Planning.domain.model.entity;
using AdhdTimeOrganizer.Planning.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.Planning.domain.serviceContract;
using FastEndpoints;
using Sydowwe.Framework.application.extensions;
using Sydowwe.Framework.domain.helper;

namespace AdhdTimeOrganizer.Planning.application.endpoint.activityPlanning.plannerTask.command;

/// <summary>
/// <c>POST /calendar/apply-planner-template/batch</c> — one template onto N days. The batched counterpart of
/// <c>calendar/apply-planner-template</c>, which takes a single calendar id, so the bulk-apply action bar had
/// to fire one request per selected day with an identical body but for that id.
/// <para>
/// <b>Each day is applied and committed on its own, in request order.</b> Conflict resolution is a
/// per-day decision — it reads the day's existing tasks — and a day whose apply fails must not undo the days
/// that already worked, which is exactly how the per-day fan-out behaved. The response says which days
/// succeeded rather than failing the whole call; the only shared, all-or-nothing precondition is the
/// template's existence, which is a 404 before any day is touched.
/// </para>
/// <para>
/// The resolution itself lives in <see cref="PlannerTemplateApplier"/>, shared with the single-day endpoint.
/// A second copy of the carving here is the one thing that would make "the same template applied the same way"
/// quietly stop being true.
/// </para>
/// </summary>
public class ApplyTemplateBatchPlannerTaskEndpoint(
    DbContext dbContext,
    IReminderRegistrationService reminders)
    : Endpoint<ApplyTemplateToCalendarsBatchRequest, CalendarBatchResultResponse>
{
    protected virtual string[] AllowedRoles() => this.GetDefaultRoles();

    public override void Configure()
    {
        Post("/calendar/apply-planner-template/batch");
        Roles(AllowedRoles());
        Validator<ApplyTemplateToCalendarsBatchValidator>();
        Summary(s =>
        {
            s.Summary = "Apply a template to several days";
            s.Description =
                "Applies one planner template, with one conflict resolution, to every named calendar day. "
                + "Answers one result per requested calendar id, in request order: partial success is normal "
                + "and days are not applied transactionally with each other.";
            s.Response<CalendarBatchResultResponse>(200, "Applied — inspect results[] for per-day outcome");
            s.Response(400, "Bad request");
            s.Response(404, "Template not found");
        });
    }

    public override async Task HandleAsync(ApplyTemplateToCalendarsBatchRequest req, CancellationToken ct)
    {
        var userId = User.GetId();

        var template = await dbContext.Set<TaskPlannerDayTemplate>().FirstOrDefaultAsync(e => e.Id == req.TemplateId, ct);
        if (template == null)
        {
            AddError("Template not found");
            await Send.ErrorsAsync(404, ct);
            return;
        }

        var results = new List<CalendarBatchResult>(req.CalendarIds.Count);

        foreach (var calendarId in req.CalendarIds)
        {
            try
            {
                // Scoped by the global user filter, so another user's id simply doesn't resolve and lands as
                // that day's succeeded:false — the same answer a stale id gets, which is what the client can
                // act on either way.
                var calendar = await dbContext.Set<Calendar>().Include(c => c.Tasks)
                    .FirstOrDefaultAsync(c => c.Id == calendarId, ct);
                if (calendar == null)
                {
                    results.Add(CalendarBatchResult.Failed(calendarId));
                    continue;
                }

                PlannerTemplateApplier.Stage(
                    dbContext,
                    calendar,
                    template,
                    req.ConflictResolution,
                    PlannerTemplateApplier.BuildTasks(req.TasksFromTemplate, userId, calendar.Id));

                var orphanedReminderIds = await reminders.GetReminderIdsForPlannerTasksAsync(
                    PlannerTemplateApplier.StagedTaskDeletions(dbContext), ct);

                await dbContext.SaveChangesAsync(ct);

                await reminders.CancelManyAsync(orphanedReminderIds, ct);

                results.Add(CalendarBatchResult.Ok(calendarId));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // This day's writes are still staged on the shared context after a failed save, and would be
                // retried — and could fail again — on every day after it. Dropping them is what keeps one bad
                // day from poisoning the rest of the batch; the template has to be re-read because clearing
                // detaches it along with everything else.
                Logger.LogWarning(ex, "Applying template {TemplateId} to calendar {CalendarId} failed; continuing with the rest of the batch",
                    req.TemplateId, calendarId);

                dbContext.ChangeTracker.Clear();
                template = await dbContext.Set<TaskPlannerDayTemplate>().FirstOrDefaultAsync(e => e.Id == req.TemplateId, ct);
                if (template == null)
                {
                    // The template went away mid-batch. Every remaining day would fail for that reason too.
                    results.Add(CalendarBatchResult.Failed(calendarId));
                    results.AddRange(req.CalendarIds.Skip(results.Count).Select(CalendarBatchResult.Failed));
                    break;
                }

                results.Add(CalendarBatchResult.Failed(calendarId));
            }
        }

        await Send.OkAsync(new CalendarBatchResultResponse { Results = results }, ct);
    }
}
