using AdhdTimeOrganizer.domain.model.entity.activityHistory;
using AdhdTimeOrganizer.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.domain.model.entity.@base;
using AdhdTimeOrganizer.domain.model.entity.todoList;

namespace AdhdTimeOrganizer.domain.model.entity.activity;

public class Activity : BaseNameTextEntity
{
    public bool IsUnavoidable { get; set; }

    public long RoleId { get; set; }
    public virtual ActivityRole Role { get; set; } = null!;
    public long? CategoryId { get; set; }
    public virtual ActivityCategory? Category { get; set; }


    // Navigation properties
    public virtual TodoList? TodoList { get; set; }
    public virtual ICollection<RoutineTodoList> RoutineTodoLists { get; set; } = [];
    public virtual ICollection<Alarm> AlarmList { get; set; } = new List<Alarm>();
    public virtual ICollection<ActivityHistory> ActivityHistoryList { get; set; } = new List<ActivityHistory>();
    public virtual ICollection<WebExtensionData> WebExtensionDataList { get; set; } = new List<WebExtensionData>();
    public virtual ICollection<PlannerTask> PlannerTaskList { get; set; } = new List<PlannerTask>();


    public Activity Clone()
    {
        var cloned = (Activity)this.MemberwiseClone();
        cloned.Id = 0; // Reset ID for new entity
        return cloned;
    }
}