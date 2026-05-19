using AdhdTimeOrganizer.domain.model.entity.activityHistory;
using AdhdTimeOrganizer.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.domain.model.entity.activityTracking.android;
using AdhdTimeOrganizer.domain.model.entity.activityTracking.desktop;
using AdhdTimeOrganizer.domain.model.entity.@base;
using AdhdTimeOrganizer.domain.model.entity.activity.memoryAnchor;
using AdhdTimeOrganizer.domain.model.entity.activity.profile;
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
    public virtual ICollection<TodoListItem> TodoListItems { get; set; } = [];
    public virtual ICollection<RoutineTodoList> RoutineTodoLists { get; set; } = [];
    public virtual ICollection<ActivityHistory> ActivityHistoryList { get; set; } = new List<ActivityHistory>();
    public virtual ICollection<PlannerTask> PlannerTaskList { get; set; } = new List<PlannerTask>();

    public TrackerDesktopMappingByPattern? TrackerDesktopMappingByPattern { get; set; }
    public TrackerAndroidMappingByPattern? TrackerAndroidMappingByPattern { get; set; }

    public ActivityBacklogProfile? BacklogProfile { get; set; }
    public ActivityDiyProfile? DiyProfile { get; set; }
    public ActivityBucketListProfile? BucketListProfile { get; set; }
    public virtual ICollection<MemoryAnchor> MemoryAnchors { get; set; } = new List<MemoryAnchor>();

    public Activity Clone()
    {
        var cloned = (Activity)this.MemberwiseClone();
        cloned.Id = 0; // Reset ID for new entity
        return cloned;
    }
}