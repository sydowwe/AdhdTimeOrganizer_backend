using AdhdTimeOrganizer.application.dto.request.@interface;
using AdhdTimeOrganizer.domain.helper;

namespace AdhdTimeOrganizer.application.dto.filter;

public class ActivityHistoryFilterRequest : IFilterRequest
{
    public long? ActivityId { get; set; }
    public long? RoleId { get; set; }
    public long? CategoryId { get; set; }
    public MyIntTime? MinLength { get; set; }
    public MyIntTime? MaxLength { get; set; }
    public long? UserId { get; set; }
    public int? HoursBack { get; set; }
    public DateTime? DateFrom { get; set; }
    public DateTime? DateTo { get; set; }
    public bool? IsFromTodoList { get; set; }
    public long? TaskUrgencyId { get; set; }
    public bool? IsFromRoutineTodoList { get; set; }
    public long? RoutineTimePeriodId { get; set; }
    public bool? IsUnavoidable { get; set; }
}
