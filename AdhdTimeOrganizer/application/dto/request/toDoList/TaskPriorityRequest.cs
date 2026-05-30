using System.ComponentModel.DataAnnotations;
using AdhdTimeOrganizer.application.dto.request.@base;
using AdhdTimeOrganizer.application.dto.request.@interface;
using AdhdTimeOrganizer.domain.model.entity.todoList;

namespace AdhdTimeOrganizer.application.dto.request.todoList;

public record TaskPriorityRequest : TextColorRequest, IMyRequest<TaskPriority>
{
    [Required]
    public short Priority { get; init; }

    public TaskPriority ToEntity => new TaskPriority
    {
        Text = Text,
        Color = Color,
        Priority = Priority
    };
    public void UpdateEntity(TaskPriority entity)
    {
        throw new NotImplementedException();
    }
}