using AdhdTimeOrganizer.domain.model.entity.activityHistory;
using AdhdTimeOrganizer.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.domain.model.entity.@base;

namespace AdhdTimeOrganizer.domain.model.entity.activity;

public class Activity : BaseNameTextEntity
{
    public bool IsOnTodoList { get; set; }
    public bool IsUnavoidable { get; set; }
    public bool IsOnRoutineTodoList { get; set; }

    public long RoleId { get; set; }
    public virtual ActivityRole Role { get; set; } = null!;
    public long? CategoryId { get; set; }
    public virtual ActivityCategory? Category { get; set; }

    public virtual TodoList? TodoList { get; set; }
    public virtual RoutineTodoList? RoutineTodoList { get; set; }

    public virtual ICollection<Alarm> AlarmList { get; set; } = new List<Alarm>();
    public virtual ICollection<ActivityHistory> ActivityHistoryList { get; set; } = new List<ActivityHistory>();
    public virtual ICollection<WebExtensionData> WebExtensionDataList { get; set; } = new List<WebExtensionData>();

    public virtual ICollection<PlannerTask> PlannerTaskList { get; set; } = new List<PlannerTask>();

}