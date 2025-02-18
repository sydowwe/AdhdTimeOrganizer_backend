using System.ComponentModel.DataAnnotations;
using AdhdTimeOrganizer.Command.application.dto.request.extendable;
using AdhdTimeOrganizer.Common.application.dto.request.@base;

namespace AdhdTimeOrganizer.Command.application.dto.request.activity;

public record ActivityRequest : NameTextRequest
{
    [Required]
    public required bool IsOnToDoList { get; init; }

    [Required]
    public required bool IsUnavoidable { get; init; }

    [Required]
    public required long RoleId { get; init; }

    public long? CategoryId { get; init; }

    public long? ToDoListUrgencyId { get; init; }

}