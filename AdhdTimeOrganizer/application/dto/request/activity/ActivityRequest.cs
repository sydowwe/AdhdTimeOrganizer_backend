using System.ComponentModel.DataAnnotations;
using AdhdTimeOrganizer.application.dto.request.@base;

namespace AdhdTimeOrganizer.application.dto.request.activity;

public record ActivityRequest : NameTextRequest
{
    [Required]
    public required bool IsOnTodoList { get; init; }

    [Required]
    public required bool IsUnavoidable { get; init; }

    [Required]
    public required long RoleId { get; init; }

    public long? CategoryId { get; init; }

    public long? TodoListUrgencyId { get; init; }

}