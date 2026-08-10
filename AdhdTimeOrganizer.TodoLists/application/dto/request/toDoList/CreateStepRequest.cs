using System.ComponentModel.DataAnnotations;

namespace AdhdTimeOrganizer.TodoLists.application.dto.request.todoList;

public record CreateStepRequest
{
    public long ItemId { get; init; }

    [MaxLength(255)]
    public required string Name { get; init; }

    public int Order { get; init; }

    [MaxLength(1000)]
    public string? Note { get; init; }
}