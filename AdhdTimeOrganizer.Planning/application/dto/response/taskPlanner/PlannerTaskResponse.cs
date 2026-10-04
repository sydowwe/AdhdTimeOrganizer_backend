using AdhdTimeOrganizer.Core.application.dto.response.activity;
using AdhdTimeOrganizer.Planning.application.dto;
using AdhdTimeOrganizer.Planning.domain.model.@enum;
using AdhdTimeOrganizer.Planning.domain.model.entity.activityPlanning;
using Sydowwe.Framework.application.dto.dto;
using Sydowwe.Framework.application.dto.response;

namespace AdhdTimeOrganizer.Planning.application.dto.response.taskPlanner;

public record PlannerTaskResponse : BasePlannerTaskResponse, IProjectionResponse<PlannerTaskResponse, PlannerTask>
{
    public required PlannerTaskStatus Status { get; init; }
    public TimeDto? ActualStartTime { get; init; }
    public TimeDto? ActualEndTime { get; init; }

    public long? SourceTemplateTaskId { get; init; }

    public string? SkipReason { get; init; }

    /// <summary><c>"noTime" | "notRelevant" | "noEnergy"</c>, or null. At most one of this and <see cref="SkipReason"/> is non-null.</summary>
    public string? SkipReasonKey { get; init; }

    public required long CalendarId { get; init; }

    public long? TodolistItemId { get; init; }
    // public required CalendarResponse Calendar { get; init; }
    // public TodoListResponse? Todolist { get; init; }

    public required string Color { get; init; }
    public required bool IsDone { get; init; }

    /// <summary>The task completes itself when its slot ends — see <see cref="PlannerTask.AutoCompleteAtEnd"/>.</summary>
    public bool AutoCompleteAtEnd { get; init; }

    /// <summary>The day the task was first planned for, when it has been carried over; otherwise null.</summary>
    public DateOnly? OriginalDate { get; init; }

    /// <summary>How many times the task has been carried over to a later day.</summary>
    public int CarryOverCount { get; init; }

    public static IQueryable<PlannerTaskResponse> Projection(IQueryable<PlannerTask> query)
    {
        return query.Select(t => new PlannerTaskResponse
        {
            Id = t.Id,
            StartTime = new TimeDto(t.StartTime.Hour, t.StartTime.Minute),
            EndTime = new TimeDto(t.EndTime.Hour, t.EndTime.Minute),
            IsBackground = t.IsBackground,
            Location = t.Location,
            Notes = t.Notes,
            Activity = new ActivityResponse
            {
                Id = t.Activity.Id,
                Name = t.Activity.Name,
                Text = t.Activity.Text,
                IsUnavoidable = t.Activity.IsUnavoidable,
                IsOnTodoList = false,
                Role = new ActivityRoleResponse
                {
                    Id = t.Activity.Role.Id,
                    Name = t.Activity.Role.Name,
                    Text = t.Activity.Role.Text,
                    Color = t.Activity.Role.Color,
                    Icon = t.Activity.Role.Icon
                },
                Category = t.Activity.Category != null
                    ? new ActivityCategoryResponse
                    {
                        Id = t.Activity.Category.Id,
                        Name = t.Activity.Category.Name,
                        Text = t.Activity.Category.Text,
                        Color = t.Activity.Category.Color,
                        Icon = t.Activity.Category.Icon
                    }
                    : null
            },
            Importance = t.Importance != null
                ? new TaskImportanceResponse
                {
                    Id = t.Importance.Id,
                    Text = t.Importance.Text,
                    Color = t.Importance.Color,
                    Icon = t.Importance.Icon,
                    Importance = t.Importance.Importance
                }
                : null,
            Status = t.Status,
            ActualStartTime = t.ActualStartTime != null ? new TimeDto(t.ActualStartTime.Value.Hour, t.ActualStartTime.Value.Minute) : null,
            ActualEndTime = t.ActualEndTime != null ? new TimeDto(t.ActualEndTime.Value.Hour, t.ActualEndTime.Value.Minute) : null,
            SourceTemplateTaskId = t.SourceTemplateTaskId,
            SkipReason = t.SkipReason,
            // Inline rather than SkipReasonKeyTokens.Token(...) so it stays translatable if a caller composes on
            // the projection (BaseSortEndpoint sorts after it).
            SkipReasonKey = t.SkipReasonKey == domain.model.@enum.SkipReasonKey.NoTime ? SkipReasonKeyTokens.NoTime
                : t.SkipReasonKey == domain.model.@enum.SkipReasonKey.NotRelevant ? SkipReasonKeyTokens.NotRelevant
                : t.SkipReasonKey == domain.model.@enum.SkipReasonKey.NoEnergy ? SkipReasonKeyTokens.NoEnergy
                : null,
            CalendarId = t.CalendarId,
            TodolistItemId = t.TodolistItemId,
            Color = t.Activity.Role.Color,
            IsDone = t.IsDone,
            AutoCompleteAtEnd = t.AutoCompleteAtEnd,
            OriginalDate = t.OriginalDate,
            CarryOverCount = t.CarryOverCount
        });
    }
}