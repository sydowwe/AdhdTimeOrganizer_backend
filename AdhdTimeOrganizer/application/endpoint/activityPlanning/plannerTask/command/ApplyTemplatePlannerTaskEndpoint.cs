using AdhdTimeOrganizer.application.dto.@enum;
using AdhdTimeOrganizer.application.dto.request.taskPlanner;
using AdhdTimeOrganizer.application.dto.response.taskPlanner;
using AdhdTimeOrganizer.application.extensions;
using AdhdTimeOrganizer.application.helper;
using AdhdTimeOrganizer.application.mapper.activityPlanning;
using AdhdTimeOrganizer.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.domain.model.@enum;
using AdhdTimeOrganizer.infrastructure.persistence;
using FastEndpoints;
using Humanizer;
using Microsoft.EntityFrameworkCore;

namespace AdhdTimeOrganizer.application.endpoint.activityPlanning.plannerTask.command;

public class ApplyTemplatePlannerTaskEndpoint(
    AppCommandDbContext dbContext,
    CalendarMapper calendarMapper,
    PlannerTaskMapper plannerTaskMapper) : Endpoint<ApplyTemplateToTaskPlannerRequest, ApplyTemplatePlannerTaskResponse>
{
    public virtual string[] AllowedRoles()
    {
        return EndpointHelper.GetUserOrHigherRoles();
    }

    public override void Configure()
    {
        const string entityName = nameof(Calendar);
        Post($"/{entityName.Kebaberize()}/apply-planner-template");
        Roles(AllowedRoles());
        Summary(s =>
        {
            s.Summary = $"Apply template to {entityName}";
            s.Description = $"Applies a planner template to a {entityName}, creating tasks from the template";
            s.Response<long>(200, "Template applied successfully");
            s.Response(400, "Bad request");
        });
    }

    public override async Task HandleAsync(ApplyTemplateToTaskPlannerRequest req, CancellationToken ct)
    {
        try
        {
            var calendar = await dbContext.Calendars.Include(t => t.Tasks)
                .FirstOrDefaultAsync(e => e.Id == req.CalendarId, ct);
            if (calendar == null)
            {
                AddError("Calendar not found");
                await SendErrorsAsync(404, ct);
                return;
            }

            var template = await dbContext.TaskPlannerDayTemplates.FirstOrDefaultAsync(e => e.Id == req.TemplateId, ct);
            if (template == null)
            {
                AddError("Template not found");
                await SendErrorsAsync(404, ct);
                return;
            }

            UpdateCalendar(template, calendar);
            template.LastUsedAt = DateTime.UtcNow;
            template.UsageCount++;

            var newTasks = req.TasksFromTemplate.Select(t => plannerTaskMapper.ToEntity(t, User.GetId())).ToList();
            var existingTasks = calendar.Tasks.ToList();

            switch (req.ConflictResolution)
            {
                case ApplyTemplateConflictResolutionEnum.Ignore:
                    newTasks = newTasks.Where(nt => !existingTasks.Any(et =>
                        et.TasksOverlap(nt.StartTime, nt.EndTime))).ToList();
                    break;

                case ApplyTemplateConflictResolutionEnum.Overwrite:
                    var conflictingExisting = existingTasks.Where(et =>
                        newTasks.Any(nt => et.TasksOverlap(nt.StartTime, nt.EndTime))).ToList();
                    dbContext.PlannerTasks.RemoveRange(conflictingExisting);
                    break;

                case ApplyTemplateConflictResolutionEnum.MergeIgnore:
                    newTasks = MergeCarveNewTasksAroundExisting(newTasks, existingTasks);
                    break;

                case ApplyTemplateConflictResolutionEnum.MergeOverwrite:
                    MergeCarveExistingTasksAroundNew(existingTasks, newTasks);
                    break;
            }

            dbContext.PlannerTasks.AddRange(newTasks);
            await dbContext.SaveChangesAsync(ct);

            calendar = await dbContext.Calendars.FindAsync([calendar.Id], ct);
            var updatedTasks = await plannerTaskMapper.ProjectToResponse(dbContext.PlannerTasks.Where(t => t.CalendarId == calendar.Id).WithIncludes().OrderBy(t => t.StartTime)).ToListAsync(ct);

            var response = new ApplyTemplatePlannerTaskResponse
            {
                Calendar = calendarMapper.ToResponse(calendar!),
                Tasks = updatedTasks
            };

            await SendAsync(response, 200, ct);
        }
        catch (Exception ex)
        {
            var result = DbUtils.HandleException(ex, "Create");
            AddError(result.ErrorMessage!);
            await SendErrorsAsync(400, ct);
        }
    }

    private void UpdateCalendar(TaskPlannerDayTemplate template, Calendar calendar)
    {
        calendar.AppliedTemplateId = template.Id;
        calendar.AppliedTemplateName = template.Name;
        calendar.WakeUpTime = template.DefaultWakeUpTime ?? calendar.WakeUpTime;
        calendar.BedTime = template.DefaultBedTime ?? calendar.BedTime;
    }



    /// <summary>
    /// MergeIgnore: Carve new tasks around existing ones.
    /// If an existing task is in the middle of a new task, split the new task.
    /// If a new task overlaps with existing, shorten it to fit.
    /// </summary>
    private List<PlannerTask> MergeCarveNewTasksAroundExisting(List<PlannerTask> newTasks, List<PlannerTask> existingTasks)
    {
        var result = new List<PlannerTask>();

        foreach (var newTask in newTasks)
        {
            var segments = CarveTaskAroundBlockers(newTask, existingTasks);
            result.AddRange(segments);
        }

        return result;
    }

    /// <summary>
    /// MergeOverwrite: Carve existing tasks around new ones.
    /// If a new task is in the middle of an existing task, split the existing task.
    /// If an existing task overlaps with new, shorten it to fit.
    /// </summary>
    private void MergeCarveExistingTasksAroundNew(List<PlannerTask> existingTasks, List<PlannerTask> newTasks)
    {
        var tasksToRemove = new List<PlannerTask>();
        var tasksToAdd = new List<PlannerTask>();

        foreach (var existingTask in existingTasks)
        {
            var overlappingNewTasks = newTasks.Where(nt =>
                existingTask.TasksOverlap(nt.StartTime, nt.EndTime)).ToList();

            if (!overlappingNewTasks.Any())
                continue;

            tasksToRemove.Add(existingTask);
            var segments = CarveTaskAroundBlockers(existingTask, overlappingNewTasks);
            tasksToAdd.AddRange(segments);
        }

        dbContext.PlannerTasks.RemoveRange(tasksToRemove);
        dbContext.PlannerTasks.AddRange(tasksToAdd);
    }

    /// <summary>
    /// Carves a task around blocking tasks, returning segments that don't overlap with blockers.
    /// Handles splitting when a blocker is in the middle, and shortening when blockers overlap edges.
    /// </summary>
    private static List<PlannerTask> CarveTaskAroundBlockers(PlannerTask task, List<PlannerTask> blockers)
    {
        var overlappingBlockers = blockers
            .Where(b => task.TasksOverlap(b.StartTime, b.EndTime))
            .OrderBy(b => b.StartTime)
            .ToList();

        if (!overlappingBlockers.Any())
            return [task];

        var result = new List<PlannerTask>();
        var currentStart = task.StartTime;

        foreach (var blocker in overlappingBlockers)
        {
            if (currentStart < blocker.StartTime)
            {
                var segmentEnd = blocker.StartTime < task.EndTime ? blocker.StartTime : task.EndTime;
                if (currentStart < segmentEnd)
                {
                    result.Add(CloneTaskWithNewTimes(task, currentStart, segmentEnd));
                }
            }

            currentStart = blocker.EndTime > currentStart ? blocker.EndTime : currentStart;
        }

        if (currentStart < task.EndTime)
        {
            result.Add(CloneTaskWithNewTimes(task, currentStart, task.EndTime));
        }

        return result;
    }

    private static PlannerTask CloneTaskWithNewTimes(PlannerTask original, TimeOnly newStart, TimeOnly newEnd)
    {
        return new PlannerTask
        {
            StartTime = newStart,
            EndTime = newEnd,
            IsBackground = original.IsBackground,
            Location = original.Location,
            Notes = original.Notes,
            ImportanceId = original.ImportanceId,
            ActivityId = original.ActivityId,
            CalendarId = original.CalendarId,
            IsDone = original.IsDone,
            Status = original.Status,
            IsFromTemplate = original.IsFromTemplate,
            SourceTemplateTaskId = original.SourceTemplateTaskId,
            TodolistId = original.TodolistId,
            UserId = original.UserId
        };
    }
}