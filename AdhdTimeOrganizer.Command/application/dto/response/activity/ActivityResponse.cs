using AdhdTimeOrganizer.Common.application.dto.response.@base;

namespace AdhdTimeOrganizer.Command.application.dto.response.activity;

public record ActivityResponse : NameTextResponse
{
    public bool IsOnToDoList { get; init; }
    public bool IsUnavoidable { get; init; }
    public required NameTextColorIconResponse Role { get; init; }
    public NameTextColorIconResponse? Category { get; init; }
}