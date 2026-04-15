using System.ComponentModel.DataAnnotations;

namespace AdhdTimeOrganizer.application.dto.request.toDoList;

public record TodoListStepRequest
{
    public Guid? Id { get; init; }
    [Required]
    [MaxLength(255)]
    public required string Name { get; init; }
    public int Order { get; init; }
    [MaxLength(1000)]
    public string? Note { get; init; }
}
