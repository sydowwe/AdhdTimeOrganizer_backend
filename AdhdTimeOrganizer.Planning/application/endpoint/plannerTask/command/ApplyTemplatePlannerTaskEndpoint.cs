using AdhdTimeOrganizer.Core.domain.serviceContract;
using AdhdTimeOrganizer.Planning.application.dto.request.taskPlanner;
using AdhdTimeOrganizer.Planning.application.dto.response.taskPlanner;
using AdhdTimeOrganizer.Planning.application.endpoint.extensions;
using AdhdTimeOrganizer.Planning.application.helper;
using AdhdTimeOrganizer.Planning.application.validator;
using AdhdTimeOrganizer.Planning.domain.model.entity;
using AdhdTimeOrganizer.Planning.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.Planning.domain.serviceContract;
using FastEndpoints;
using Humanizer;
using Sydowwe.Framework.application.extensions;
using Sydowwe.Framework.domain.helper;
using Sydowwe.Framework.infrastructure.persistence;

namespace AdhdTimeOrganizer.Planning.application.endpoint.activityPlanning.plannerTask.command;

/// <summary>
/// One template onto one day, returning that day and its tasks. The conflict resolution itself lives in
/// <see cref="PlannerTemplateApplier"/>, shared with <c>ApplyTemplateBatchPlannerTaskEndpoint</c>.
/// </summary>
public class ApplyTemplatePlannerTaskEndpoint(
    DbContext dbContext,
    IReminderRegistrationService reminders,
    ICalendarProvisioner calendars,
    IReferencedIdOwnershipService ownership)
    : Endpoint<ApplyTemplateToTaskPlannerRequest, ApplyTemplatePlannerTaskResponse>
{
    protected virtual string[] AllowedRoles() => this.GetDefaultRoles();

    public override void Configure()
    {
        const string entityName = nameof(Calendar);
        Post($"/{entityName.Kebaberize()}/apply-planner-template");
        Roles(AllowedRoles());
        Validator<ApplyTemplateToTaskPlannerValidator>();

        Summary(s =>
        {
            s.Summary = $"Apply template to {entityName}";
            s.Description = $"Applies a planner template to a {entityName}, creating tasks from the template";
            s.Response<ApplyTemplatePlannerTaskResponse>(201, "Template applied successfully; new tasks created");
            s.Response(400, "Bad request");
            s.Response(404, "Calendar or template not found");
            s.Response(409, "A concurrent write to the same day lost the row_version race");
        });
    }

    public override async Task HandleAsync(ApplyTemplateToTaskPlannerRequest req, CancellationToken ct)
    {
        try
        {
            // The template's own lines come off the wire, so every ActivityId / ImportanceId / TodolistId in
            // them is unproven exactly as a single POST /planner-task's is. Checked before provisioning, which
            // commits — a request that is about to 404 must not leave a calendar row behind.
            if (!await this.EnsureOwnsPlannerTaskReferencesAsync(dbContext, ownership, req.TasksFromTemplate, ct))
                return;

            // Provisioning first, and it commits — so nothing of this request's own is staged yet. A template
            // applied by date to a day the user has never opened creates that day; by id it still 404s, because
            // an id that resolves to nothing is a stale client, not an unplanned day.
            var calendarId = req.CalendarId
                             ?? (await calendars.EnsureForDateAsync(User.GetId(), req.Date!.Value, ct)).Id;

            var calendar = await dbContext.Set<Calendar>().Include(t => t.Tasks)
                .FirstOrDefaultAsync(e => e.Id == calendarId, ct);
            if (calendar == null)
            {
                AddError("Calendar not found");
                await Send.ErrorsAsync(404, ct);
                return;
            }

            var template = await dbContext.Set<TaskPlannerDayTemplate>().FirstOrDefaultAsync(e => e.Id == req.TemplateId, ct);
            if (template == null)
            {
                AddError("Template not found");
                await Send.ErrorsAsync(404, ct);
                return;
            }

            PlannerTemplateApplier.Stage(
                dbContext,
                calendar,
                template,
                req.ConflictResolution,
                PlannerTemplateApplier.BuildTasks(req.TasksFromTemplate, User.GetId(), calendar.Id));

            var orphanedReminderIds = await reminders.GetReminderIdsForPlannerTasksAsync(
                PlannerTemplateApplier.StagedTaskDeletions(dbContext), ct);

            await dbContext.SaveChangesAsync(ct);

            await reminders.CancelManyAsync(orphanedReminderIds, ct);

            var updatedTasks = await PlannerTaskResponse.Projection(dbContext.Set<PlannerTask>().Where(t => t.CalendarId == calendar.Id).WithIncludes().OrderBy(t => t.StartTime)).ToListAsync(ct);

            var response = new ApplyTemplatePlannerTaskResponse
            {
                Calendar = CalendarResponse.Projection(new[] { calendar! }.AsQueryable()).Single(),
                Tasks = updatedTasks
            };

            await Send.ResponseAsync(response, 201, ct);
        }
        catch (Exception ex)
        {
            // The classification DbUtils already made, rather than collapsing everything to 400: two devices
            // applying templates to the same day race on the calendar's row_version, and a client that retries
            // on 409 but surfaces a validation message on 400 would show "invalid request" for a transient
            // conflict. Same mapping PatchPlannerTaskStatusEndpoint uses.
            var result = DbUtils.HandleException(ex, "Create");
            AddError(result.ErrorMessage!);
            await Send.ErrorsAsync(EndpointHelper.ToStatusCode(result.ErrorType), ct);
        }
    }
}
