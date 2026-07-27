using AdhdTimeOrganizer.application.dto.request.@base;
using AdhdTimeOrganizer.domain.model.entity.todoList;
using Sydowwe.Framework.application.dto.request.@interface;

namespace AdhdTimeOrganizer.application.dto.request.todoList;

public record TaskPriorityRequest : TextColorRequest, IMyRequest<TaskPriority>
{
    public short Priority { get; init; }

    public TaskPriority ToEntity => new()
    {
        UserId = 0,
        Text = Text,
        Color = Color,
        Priority = Priority
    };

    public void UpdateEntity(TaskPriority entity)
    {
        throw new NotImplementedException();
    }
}