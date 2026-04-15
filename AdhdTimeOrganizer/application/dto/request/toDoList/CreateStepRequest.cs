using System.ComponentModel.DataAnnotations;
using AdhdTimeOrganizer.application.dto.request.@interface;

namespace AdhdTimeOrganizer.application.dto.request.toDoList;

public record CreateStepRequest : IMyRequest
{
    [Required] public long ItemId { get; init; }
    [Required][MaxLength(255)] public required string Name { get; init; }
    public int Order { get; init; }
    [MaxLength(1000)] public string? Note { get; init; }
}
