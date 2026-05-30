using AdhdTimeOrganizer.application.dto.dto;
using AdhdTimeOrganizer.application.dto.response.activity;
using AdhdTimeOrganizer.domain.model.@enum;
using AdhdTimeOrganizer.domain.model.entity.activityPlanning;

namespace AdhdTimeOrganizer.application.dto.response.taskPlanner;

public record PlannerTaskResponse : BasePlannerTaskResponse, IProjectionResponse<PlannerTaskResponse, PlannerTask>
{
    public required PlannerTaskStatus Status { get; init; }
    public TimeDto? ActualStartTime { get; init; }
    public TimeDto? ActualEndTime { get; init; }

    public long? SourceTemplateTaskId { get; init; }

    public string? SkipReason { get; init; }

    public required long CalendarId { get; init; }
    public long? TodolistItemId { get; init; }
    // public required CalendarResponse Calendar { get; init; }
    // public TodoListResponse? Todolist { get; init; }

    public required string Color { get; init; }
    public required bool IsDone { get; init; }

    public static IQueryable<PlannerTaskResponse> Projection(IQueryable<PlannerTask> query) =>
        query.Select(t => new PlannerTaskResponse
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
                        Icon = t.Activity.Category.Icon,
                        Role = null
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
            CalendarId = t.CalendarId,
            TodolistItemId = t.TodolistItemId,
            Color = t.Activity.Role.Color,
            IsDone = t.IsDone
        });

    public static PlannerTaskResponse FromEntity(PlannerTask entity) => new()
    {
        Id = entity.Id,
        StartTime = new TimeDto(entity.StartTime.Hour, entity.StartTime.Minute),
        EndTime = new TimeDto(entity.EndTime.Hour, entity.EndTime.Minute),
        IsBackground = entity.IsBackground,
        Location = entity.Location,
        Notes = entity.Notes,
        Activity = new ActivityResponse
        {
            Id = entity.Activity.Id,
            Name = entity.Activity.Name,
            Text = entity.Activity.Text,
            IsUnavoidable = entity.Activity.IsUnavoidable,
            IsOnTodoList = false,
            Role = new ActivityRoleResponse
            {
                Id = entity.Activity.Role.Id,
                Name = entity.Activity.Role.Name,
                Text = entity.Activity.Role.Text,
                Color = entity.Activity.Role.Color,
                Icon = entity.Activity.Role.Icon
            },
            Category = entity.Activity.Category != null
                ? new ActivityCategoryResponse
                {
                    Id = entity.Activity.Category.Id,
                    Name = entity.Activity.Category.Name,
                    Text = entity.Activity.Category.Text,
                    Color = entity.Activity.Category.Color,
                    Icon = entity.Activity.Category.Icon,
                    Role = null
                }
                : null
        },
        Importance = entity.Importance != null
            ? new TaskImportanceResponse
            {
                Id = entity.Importance.Id,
                Text = entity.Importance.Text,
                Color = entity.Importance.Color,
                Icon = entity.Importance.Icon,
                Importance = entity.Importance.Importance
            }
            : null,
        Status = entity.Status,
        ActualStartTime = entity.ActualStartTime != null ? new TimeDto(entity.ActualStartTime.Value.Hour, entity.ActualStartTime.Value.Minute) : null,
        ActualEndTime = entity.ActualEndTime != null ? new TimeDto(entity.ActualEndTime.Value.Hour, entity.ActualEndTime.Value.Minute) : null,
        SourceTemplateTaskId = entity.SourceTemplateTaskId,
        SkipReason = entity.SkipReason,
        CalendarId = entity.CalendarId,
        TodolistItemId = entity.TodolistItemId,
        Color = entity.Activity.Role.Color,
        IsDone = entity.IsDone
    };
}