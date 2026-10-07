using AdhdTimeOrganizer.Planning.application.dto.request.taskPlanner;
using AdhdTimeOrganizer.Planning.application.dto.response.taskPlanner;
using AdhdTimeOrganizer.Planning.application.validator;
using AdhdTimeOrganizer.Planning.domain.model.entity;
using AdhdTimeOrganizer.Planning.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.Planning.domain.model.@enum;
using FastEndpoints;
using Sydowwe.Framework.application.extensions;
using Sydowwe.Framework.domain.helper;

namespace AdhdTimeOrganizer.Planning.application.endpoint.activityPlanning.plannerTask.command;

/// <summary>
/// <c>POST /planner-task/copy-to-days</c> — copy one day's plan onto several other days. Replaces a fan-out
/// of <c>POST /planner-task</c>, one per (target day × source task): twelve tasks onto ten days was 120
/// requests.
/// <para>
/// The source day's tasks are read here rather than sent, so the copy is of the day as it currently stands.
/// Each target day is written and committed on its own, in request order — a target that fails leaves the
/// others copied, matching what the per-task fan-out did.
/// </para>
/// <para>
/// <b>What a copy carries is the plan, not its execution.</b> Span, lane, location, notes, activity and
/// importance come across; <c>Status</c> resets to <c>NotStarted</c> and the actual start/end times, skip
/// reason and Google event id do not come across at all — a day copied onto next Tuesday would otherwise
/// arrive already marked completed, at times it was never worked. The to-do link is dropped for the same
/// reason in the other direction: a to-do item pairs with the one planner task it was planned as, and copying
/// the id would point several tasks at it and fan every done-toggle out across all of them.
/// </para>
/// <para>
/// <c>SourceRepeatingTaskId</c> is dropped too (FO6): a copy is a new plan, not the rule's occurrence. Carried,
/// it would show the repeat mark on a block the rule never placed, follow the rule's edits as if untouched, and
/// stop the rule placing its own occurrence on the target day.
/// </para>
/// </summary>
public class CopyToDaysPlannerTaskEndpoint(DbContext dbContext)
    : Endpoint<CopyPlannerTasksToDaysRequest, CalendarBatchResultResponse>
{
    protected virtual string[] AllowedRoles() => this.GetDefaultRoles();

    public override void Configure()
    {
        Post("/planner-task/copy-to-days");
        Roles(AllowedRoles());
        Validator<CopyPlannerTasksToDaysValidator>();
        Summary(s =>
        {
            s.Summary = "Copy a day's planner tasks onto other days";
            s.Description =
                "Copies every planner task on the source day onto each target day, resetting execution state. "
                + "Answers one result per requested target calendar id, in request order: partial success is "
                + "normal and target days are not written transactionally with each other.";
            s.Response<CalendarBatchResultResponse>(200, "Copied — inspect results[] for per-day outcome");
            s.Response(400, "Bad request");
            s.Response(404, "Source day not found");
        });
    }

    public override async Task HandleAsync(CopyPlannerTasksToDaysRequest req, CancellationToken ct)
    {
        var userId = User.GetId();

        // Scoped by the global user filter — another user's source day does not resolve, so this is the
        // ownership check as well as the existence one.
        var sourceExists = await dbContext.Set<Calendar>().AnyAsync(c => c.Id == req.SourceCalendarId, ct);
        if (!sourceExists)
        {
            AddError("Source calendar not found");
            await Send.ErrorsAsync(404, ct);
            return;
        }

        var sourceTasks = await dbContext.Set<PlannerTask>()
            .AsNoTracking()
            .Where(t => t.CalendarId == req.SourceCalendarId)
            .OrderBy(t => t.StartTime)
            .ThenBy(t => t.EndTime)
            .ToListAsync(ct);

        // One round trip for every target day's existence instead of an AnyAsync per iteration — at the
        // PlannerBatchLimits ceiling that was 366 extra queries on top of the 366 saves. Scoped by the same
        // global user filter, so a foreign id simply does not come back and is reported as failed, exactly as
        // the per-day check did. The per-day save stays: target days are deliberately not transactional with
        // each other.
        var existingTargetIds = await dbContext.Set<Calendar>()
            .Where(c => req.TargetCalendarIds.Contains(c.Id))
            .Select(c => c.Id)
            .ToHashSetAsync(ct);

        var results = new List<CalendarBatchResult>(req.TargetCalendarIds.Count);

        foreach (var targetCalendarId in req.TargetCalendarIds)
        {
            try
            {
                if (!existingTargetIds.Contains(targetCalendarId))
                {
                    results.Add(CalendarBatchResult.Failed(targetCalendarId, CalendarBatchFailureReason.NotFound));
                    continue;
                }

                // An empty source day is a successful copy of nothing, not a failure — the user asked for the
                // target days to look like the source day, and they do.
                if (sourceTasks.Count > 0)
                {
                    dbContext.Set<PlannerTask>().AddRange(sourceTasks.Select(t => CopyOnto(t, targetCalendarId, userId)));
                    await dbContext.SaveChangesAsync(ct);
                }

                results.Add(CalendarBatchResult.Ok(targetCalendarId));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Same reasoning as the batched template apply: this day's staged copies survive a failed save
                // and would be re-attempted on every day after it, so drop them before moving on.
                Logger.LogWarning(ex, "Copying calendar {SourceCalendarId} onto calendar {TargetCalendarId} failed; continuing with the rest of the batch",
                    req.SourceCalendarId, targetCalendarId);

                dbContext.ChangeTracker.Clear();
                results.Add(CalendarBatchResult.Failed(targetCalendarId, CalendarBatchFailureReason.Error));
            }
        }

        await Send.OkAsync(new CalendarBatchResultResponse { Results = results }, ct);
    }

    private static PlannerTask CopyOnto(PlannerTask source, long calendarId, long userId) =>
        new()
        {
            UserId = userId,
            CalendarId = calendarId,
            ActivityId = source.ActivityId,
            StartTime = source.StartTime,
            EndTime = source.EndTime,
            IsBackground = source.IsBackground,
            Location = source.Location,
            Notes = source.Notes,
            ImportanceId = source.ImportanceId,
            SourceTemplateTaskId = source.SourceTemplateTaskId,
            Status = PlannerTaskStatus.NotStarted
        };
}
