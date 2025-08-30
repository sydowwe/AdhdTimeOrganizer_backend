using AdhdTimeOrganizer.application.dto.request.@interface;

namespace AdhdTimeOrganizer.application.dto.request.activity;

public record ActivitySelectForm : IMyRequest
{
    public long? ActivityId { get; init; }
    public long? RoleId { get; init; }
    public long? CategoryId { get; init; }
    public bool? IsUnavoidable { get; init; }
    public bool? IsFromToDoList { get; init; }
    public long? TaskUrgencyId { get; init; }
    public bool? IsFromRoutineToDoList { get; init; }
    public long? RoutineTimePeriodId { get; init; }
}