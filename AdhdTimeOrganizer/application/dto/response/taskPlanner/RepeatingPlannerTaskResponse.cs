using AdhdTimeOrganizer.application.dto.dto;
using AdhdTimeOrganizer.application.dto.response.activity;
using AdhdTimeOrganizer.application.dto.response.@base;
using AdhdTimeOrganizer.domain.model.@enum;
using AdhdTimeOrganizer.domain.model.entity.activityPlanning;

namespace AdhdTimeOrganizer.application.dto.response.taskPlanner;

public record RepeatingPlannerTaskResponse : IdResponse, IProjectionResponse<RepeatingPlannerTaskResponse, RepeatingPlannerTask>
{
    public required ActivityResponse Activity { get; init; }
    public required TaskImportanceResponse? Importance { get; init; }
    public required TimeDto StartTime { get; init; }
    public required TimeDto EndTime { get; init; }
    public required bool IsBackground { get; init; }
    public string? Location { get; init; }
    public string? Notes { get; init; }
    public required string Color { get; init; }
    public required bool IsActive { get; init; }
    public required RecurrenceType RecurrenceType { get; init; }
    public required IEnumerable<string> ScheduledDays { get; init; }
    public required IEnumerable<int> ScheduledDates { get; init; }
    public DateOnly? ActiveFromDate { get; init; }
    public DateOnly? ActiveToDate { get; init; }
    public required IEnumerable<string> ScheduledForDayTypes { get; init; }

    public static IQueryable<RepeatingPlannerTaskResponse> Projection(IQueryable<RepeatingPlannerTask> query) =>
        query.Select(t => new RepeatingPlannerTaskResponse
        {
            Id = t.Id,
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
            StartTime = new TimeDto(t.StartTime.Hour, t.StartTime.Minute),
            EndTime = new TimeDto(t.EndTime.Hour, t.EndTime.Minute),
            IsBackground = t.IsBackground,
            Location = t.Location,
            Notes = t.Notes,
            Color = t.Activity.Role.Color,
            IsActive = t.IsActive,
            RecurrenceType = t.RecurrenceType,
            ScheduledDays = t.ScheduledDays,
            ScheduledDates = t.ScheduledDates,
            ActiveFromDate = t.ActiveFromDate,
            ActiveToDate = t.ActiveToDate,
            ScheduledForDayTypes = t.ScheduledForDayTypes
        });

    public static RepeatingPlannerTaskResponse FromEntity(RepeatingPlannerTask entity) => new()
    {
        Id = entity.Id,
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
        StartTime = new TimeDto(entity.StartTime.Hour, entity.StartTime.Minute),
        EndTime = new TimeDto(entity.EndTime.Hour, entity.EndTime.Minute),
        IsBackground = entity.IsBackground,
        Location = entity.Location,
        Notes = entity.Notes,
        Color = entity.Activity.Role.Color,
        IsActive = entity.IsActive,
        RecurrenceType = entity.RecurrenceType,
        ScheduledDays = entity.ScheduledDays,
        ScheduledDates = entity.ScheduledDates,
        ActiveFromDate = entity.ActiveFromDate,
        ActiveToDate = entity.ActiveToDate,
        ScheduledForDayTypes = entity.ScheduledForDayTypes
    };
}
