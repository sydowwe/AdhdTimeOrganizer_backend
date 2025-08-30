using AdhdTimeOrganizer.application.dto.response.@base;

namespace AdhdTimeOrganizer.application.dto.response.activity;

public record ActivityResponse : NameTextResponse
{
    public bool IsOnTodoList { get; init; }
    public bool IsUnavoidable { get; init; }
    public required NameTextColorIconResponse Role { get; init; }
    public NameTextColorIconResponse? Category { get; init; }
}