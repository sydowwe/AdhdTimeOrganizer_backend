using System.Text.Json.Serialization;
using AdhdTimeOrganizer.Core.application.dto.response.activity;
using AdhdTimeOrganizer.TodoLists.application.dto.request.todoList;
using AdhdTimeOrganizer.TodoLists.domain.model.entity.todoList;
using Sydowwe.Framework.application.dto.dto;
using Sydowwe.Framework.application.dto.response;

namespace AdhdTimeOrganizer.TodoLists.application.dto.response.todoList;

public record TodoListItemResponse : BaseTodoListResponse,
    IProjectionResponse<TodoListItemResponse, TodoListItem>
{
    public required TaskPriorityResponse TaskPriority { get; init; }
    public DateOnly? DueDate { get; init; }

    /// <summary>
    /// <c>{hours, minutes}</c>, per the portal's time-of-day convention — see the remarks on
    /// <see cref="CreateTodoListItemRequest.DueTime"/>. The entity is still a <c>TimeOnly</c>; this is
    /// the boundary that converts, the same one <c>PlannerTaskResponse</c> uses.
    /// </summary>
    public TimeDto? DueTime { get; init; }

    /// <summary>Activity id of the paired leisure activity, or null when the task is unpaired.</summary>
    public long? PairedLeisureActivityId { get; init; }

    /// <summary>
    /// The item's next open planner block from the user's today on, or null when none is planned. <b>Not</b>
    /// projected — the planner is another slice's — so it is filled after the query by
    /// <c>TodoListItemPlannedSlotFiller</c>, on the reads that call it (get-all and get-by-id). Distinct from
    /// <see cref="DueDate"/>, which is the user's deadline and is never written by the planner.
    /// </summary>
    public PlannedSlotResponse? NextPlannedSlot { get; init; }

    /// <summary>
    /// FO3: <c>"smallerStep"</c>, <c>"shorterBlock"</c> or <c>"reconsider"</c> when the item keeps slipping in the
    /// planner and none of its attempts completed. Null when it is not stuck, or is done. It says what kind of help
    /// to offer and <b>never how many times</b> the item slipped (see <c>TodoListItemPlanHelp</c>). Like
    /// <see cref="NextPlannedSlot"/> it is not projected. It is filled after the query on get-all, get-by-id and
    /// the dashboard widget.
    /// </summary>
    public string? PlanHelp { get; init; }

    /// <summary>
    /// The item's own postpone count, projected only so <see cref="PlanHelp"/> can be decided without a second
    /// query. <b>Server-side, never on the wire</b>: the feature offers help, and a count would make it a score.
    /// </summary>
    [JsonIgnore]
    public int PostponeCount { get; init; }

    public static IQueryable<TodoListItemResponse> Projection(IQueryable<TodoListItem> q)
    {
        return q.Select(e => new TodoListItemResponse
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
                    Icon = e.Activity.Role.Icon
                },
                Category = e.Activity.Category == null
                    ? null
                    : new ActivityCategoryResponse
                    {
                        Id = e.Activity.Category.Id,
                        Name = e.Activity.Category.Name,
                        Text = e.Activity.Category.Text,
                        Color = e.Activity.Category.Color,
                        Icon = e.Activity.Category.Icon
                    }
            },
            IsDone = e.IsDone,
            DisplayOrder = e.DisplayOrder,
            DoneCount = e.DoneCount,
            TotalCount = e.TotalCount,
            Note = e.Note,
            SuggestedTime = e.SuggestedTime,
            Steps = e.Steps.Select(s => new TodoListStepResponse
            {
                Id = s.Id,
                Name = s.Name,
                Order = s.Order,
                IsDone = s.IsDone,
                Note = s.Note
            }).ToList(),
            TaskPriority = new TaskPriorityResponse
            {
                Id = e.TaskPriority.Id,
                Text = e.TaskPriority.Text,
                Color = e.TaskPriority.Color,
                Priority = e.TaskPriority.Priority
            },
            DueDate = e.DueDate,
            // Inlined rather than TimeOnly.ToDto: extension properties can't appear in an expression
            // tree (CS9296) — the same note TemplatePlannerTaskResponse carries.
            DueTime = e.DueTime != null ? new TimeDto(e.DueTime.Value.Hour, e.DueTime.Value.Minute) : null,
            PairedLeisureActivityId = e.PairedLeisureActivityId,
            PostponeCount = e.PostponeCount
        });
    }
}