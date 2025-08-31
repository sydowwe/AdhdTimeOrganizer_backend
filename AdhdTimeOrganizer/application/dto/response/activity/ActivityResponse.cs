using AdhdTimeOrganizer.application.dto.response.@base;

namespace AdhdTimeOrganizer.application.dto.response.activity;

public record ActivityResponse : NameTextResponse
{
    public bool IsOnTodoList { get; init; }
    public bool IsUnavoidable { get; init; }
    public required ActivityRoleResponse Role { get; init; }
    public ActivityCategoryResponse? Category { get; init; }
}