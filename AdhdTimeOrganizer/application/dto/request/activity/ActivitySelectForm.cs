using AdhdTimeOrganizer.application.dto.request.@interface;

namespace AdhdTimeOrganizer.application.dto.request.activity;

public record ActivitySelectForm : IMyRequest
{
    public long? ActivityId { get; init; }
    public long? RoleId { get; init; }
    public long? CategoryId { get; init; }
    public bool? IsUnavoidable { get; init; }
    public bool? IsFromTodoList { get; init; }
    public long? TaskPriorityId { get; init; }
    public bool? IsFromRoutineTodoList { get; init; }
    public long? RoutineTimePeriodId { get; init; }
}