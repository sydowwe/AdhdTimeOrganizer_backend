using AdhdTimeOrganizer.application.dto.response.activity;
using AdhdTimeOrganizer.application.dto.response.taskPlanner;
using AdhdTimeOrganizer.domain.model.entity.todoList;


namespace AdhdTimeOrganizer.application.dto.response.todoList;

public record TodoListItemResponse : BaseTodoListResponse,
    IProjectionResponse<TodoListItemResponse, TodoListItem>
{
    public required TaskPriorityResponse TaskPriority { get; init; }
    public DateOnly? DueDate { get; init; }
    public TimeOnly? DueTime { get; init; }

    public static IQueryable<TodoListItemResponse> Projection(IQueryable<TodoListItem> q) =>
        q.Select(e => new TodoListItemResponse
        {
            Id = e.Id,
            Activity = new ActivityResponse
            {
                Id = e.Activity.Id,
                Name = e.Activity.Name,
                Text = e.Activity.Text,
                IsUnavoidable = e.Activity.IsUnavoidable,
                Role = new ActivityRoleResponse
                {
                    Id = e.Activity.Role.Id,
                    Name = e.Activity.Role.Name,
                    Text = e.Activity.Role.Text,
                    Color = e.Activity.Role.Color,
                    Icon = e.Activity.Role.Icon,
                },
                Category = e.Activity.Category == null ? null : new ActivityCategoryResponse
                {
                    Id = e.Activity.Category.Id,
                    Name = e.Activity.Category.Name,
                    Text = e.Activity.Category.Text,
                    Color = e.Activity.Category.Color,
                    Icon = e.Activity.Category.Icon,
                },
            },
            IsDone = e.IsDone,
            DisplayOrder = e.DisplayOrder,
            DoneCount = e.DoneCount,
            TotalCount = e.TotalCount,
            Note = e.Note,
            Steps = e.Steps.Select(s => new TodoListStepResponse
            {
                Id = s.Id,
                Name = s.Name,
                Order = s.Order,
                IsDone = s.IsDone,
                Note = s.Note,
            }).ToList(),
            TaskPriority = new TaskPriorityResponse
            {
                Id = e.TaskPriority.Id,
                Text = e.TaskPriority.Text,
                Color = e.TaskPriority.Color,
                Priority = e.TaskPriority.Priority,
            },
            DueDate = e.DueDate,
            DueTime = e.DueTime,
        });

    public static TodoListItemResponse FromEntity(TodoListItem e) => new()
    {
        Id = e.Id,
        Activity = new ActivityResponse
        {
            Id = e.Activity.Id,
            Name = e.Activity.Name,
            Text = e.Activity.Text,
            IsUnavoidable = e.Activity.IsUnavoidable,
            Role = new ActivityRoleResponse
            {
                Id = e.Activity.Role.Id,
                Name = e.Activity.Role.Name,
                Text = e.Activity.Role.Text,
                Color = e.Activity.Role.Color,
                Icon = e.Activity.Role.Icon,
            },
            Category = e.Activity.Category == null ? null : new ActivityCategoryResponse
            {
                Id = e.Activity.Category.Id,
                Name = e.Activity.Category.Name,
                Text = e.Activity.Category.Text,
                Color = e.Activity.Category.Color,
                Icon = e.Activity.Category.Icon,
            },
        },
        IsDone = e.IsDone,
        DisplayOrder = e.DisplayOrder,
        DoneCount = e.DoneCount,
        TotalCount = e.TotalCount,
        Note = e.Note,
        Steps = e.Steps.Select(s => new TodoListStepResponse
        {
            Id = s.Id,
            Name = s.Name,
            Order = s.Order,
            IsDone = s.IsDone,
            Note = s.Note,
        }).ToList(),
        TaskPriority = new TaskPriorityResponse
        {
            Id = e.TaskPriority.Id,
            Text = e.TaskPriority.Text,
            Color = e.TaskPriority.Color,
            Priority = e.TaskPriority.Priority,
        },
        DueDate = e.DueDate,
        DueTime = e.DueTime,
    };
}
