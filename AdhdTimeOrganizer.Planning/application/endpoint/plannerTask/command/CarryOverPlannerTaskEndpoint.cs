using AdhdTimeOrganizer.Planning.application.dto.request.taskPlanner;
using AdhdTimeOrganizer.Planning.application.dto.response.taskPlanner;
using AdhdTimeOrganizer.Planning.application.validator;
using AdhdTimeOrganizer.Planning.domain.model.@enum;
using AdhdTimeOrganizer.Planning.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.Planning.domain.serviceContract;
using FastEndpoints;
using Sydowwe.Framework.application.error;
using Sydowwe.Framework.application.extensions;
using Sydowwe.Framework.domain.helper;
using Sydowwe.Framework.infrastructure.persistence;

namespace AdhdTimeOrganizer.Planning.application.endpoint.activityPlanning.plannerTask.command;

/// <summary>
/// <c>POST /planner-task/{id}/carry-over</c> — moves an unfinished task forward to a later day <b>and keeps the
/// record that it slipped</b>: <see cref="PlannerTask.OriginalDate"/> is set the first time and
/// <see cref="PlannerTask.CarryOverCount"/> goes up every time.
/// <para>
/// The client's "not completed yesterday" banner used to carry a task over by <i>copying</i> it, which left the
/// original open on its day — that day could then never count for the streak, and the trend logged it as
/// "never happened", so carrying a task over scored worse than skipping it. A plain move fixed that but threw the
/// slip away, and "pushed four times" is the most useful signal this app has for a task that needs splitting or
/// dropping. This keeps both.
/// </para>
/// <para>
/// The task leaves its old day entirely, so that day's streak and trend no longer see it — the same neutral
/// outcome a skip has. It arrives <c>NotStarted</c> with its actual times cleared: a carried task is a fresh
/// attempt, and yesterday's start time on today's block would be a lie. Its time of day is kept.
/// </para>
/// </summary>
public class CarryOverPlannerTaskEndpoint(
    DbContext dbContext,
    ICalendarProvisioner calendars,
    IReminderRegistrationService reminders) : Endpoint<CarryOverPlannerTaskRequest, PlannerTaskResponse>
{
    protected virtual string[] AllowedRoles() => this.GetDefaultRoles();

    public override void Configure()
    {
        Post("/planner-task/{id:long:required}/carry-over");
        Roles(AllowedRoles());
        Validator<CarryOverPlannerTaskValidator>();
        Summary(s =>
        {
            s.Summary = "Carry an unfinished planner task over to a later day";
            s.Description = "Moves an open (not completed, not skipped) task to the given later day, creating that day if needed. "
                            + "Records the day it was first planned for (OriginalDate, set once) and increments CarryOverCount. "
                            + "The task arrives NotStarted with its actual times cleared; its time of day is kept.";
            s.Response<PlannerTaskResponse>(200, "The task as it now stands");
            s.Response(400, "The task is already completed or skipped, or Date is not after its current day");
            s.Response(404, "Not found");
            s.Response(409, "Concurrent write to the same task lost the row_version race");
        });
    }

    public override async Task HandleAsync(CarryOverPlannerTaskRequest req, CancellationToken ct)
    {
        try
        {
            // Scoped by the host's global IEntityWithUser filter, so a stranger's task reads as missing.
            var task = await dbContext.Set<PlannerTask>()
                .Include(t => t.Calendar)
                .FirstOrDefaultAsync(t => t.Id == Route<long>("id"), ct);

            if (task is null)
            {
                await Send.NotFoundAsync(ct);
                return;
            }

            if (task.Status is PlannerTaskStatus.Completed or PlannerTaskStatus.Cancelled)
            {
                AddError(r => r.Date, "Only an open task can be carried over; this one is already completed or skipped.");
                await Send.ErrorsAsync(400, ct);
                return;
            }

            var fromDate = task.Calendar.Date;
            if (req.Date <= fromDate)
            {
                AddError(r => r.Date, "Date must be after the task's current day.");
                await Send.ErrorsAsync(400, ct);
                return;
            }

            // Resolved before anything is staged: provisioning commits, and would take a half-made write with it.
            var target = await calendars.EnsureForDateAsync(User.GetId(), req.Date, ct);

            task.OriginalDate ??= fromDate;
            task.CarryOverCount++;
            task.CalendarId = target.Id;
            task.ApplyStatus(PlannerTaskStatus.NotStarted);

            await dbContext.SaveChangesAsync(ct);

            // The task's day changed, so a task-linked reminder's instant did too (Reminder.RemindAt gotcha).
            await reminders.SyncForPlannerTasksAsync([task.Id], ct);

            var response = await PlannerTaskResponse.Projection(
                    dbContext.Set<PlannerTask>().AsNoTracking().Where(t => t.Id == task.Id))
                .FirstAsync(ct);
            await Send.OkAsync(response, ct);
        }
        catch (Exception ex)
        {
            // The mapping every write endpoint uses: a lost row_version race is a 409, not a 500.
            var result = DbUtils.HandleException(ex, nameof(HandleAsync));
            AddError(result.ErrorMessage!);
            await Send.ErrorsAsync(this.ErrorStatus(result.ErrorType), ct);
        }
    }
}
