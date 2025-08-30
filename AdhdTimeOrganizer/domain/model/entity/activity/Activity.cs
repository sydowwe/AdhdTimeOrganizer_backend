using AdhdTimeOrganizer.domain.model.entity.activityHistory;
using AdhdTimeOrganizer.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.domain.model.entity.@base;

namespace AdhdTimeOrganizer.domain.model.entity.activity;

public class Activity : BaseNameTextEntity
{
    public bool IsOnToDoList { get; set; }
    public bool IsUnavoidable { get; set; }
    public bool IsOnRoutineToDoList { get; set; }

    public long RoleId { get; set; }
    public virtual ActivityRole Role { get; set; }
    public long? CategoryId { get; set; }
    public virtual ActivityCategory? Category { get; set; }

    public virtual ToDoList? ToDoList { get; set; }
    public virtual RoutineToDoList? RoutineToDoList { get; set; }

    public virtual ICollection<Alarm> AlarmList { get; set; } = new List<Alarm>();
    public virtual ICollection<ActivityHistory> ActivityHistoryList { get; set; } = new List<ActivityHistory>();
    public virtual ICollection<WebExtensionData> WebExtensionDataList { get; set; } = new List<WebExtensionData>();

    public virtual ICollection<PlannerTask> PlannerTaskList { get; set; } = new List<PlannerTask>();

}