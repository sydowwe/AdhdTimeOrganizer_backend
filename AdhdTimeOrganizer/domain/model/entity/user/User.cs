using System.ComponentModel.DataAnnotations.Schema;
using AdhdTimeOrganizer.domain.model.entity.activity;
using AdhdTimeOrganizer.domain.model.entity.activityHistory;
using AdhdTimeOrganizer.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.domain.model.entityInterface;
using AdhdTimeOrganizer.domain.model.@enum;
using Microsoft.AspNetCore.Identity;

namespace AdhdTimeOrganizer.domain.model.entity.user;

public class User : IdentityUser<long>, IBaseTableEntity
{
    public DateTime CreatedTimestamp { get; set; }
    public DateTime ModifiedTimestamp { get; set; }
    public required AvailableLocales CurrentLocale { get; set; }
    public required TimeZoneInfo Timezone { get; set; }
    public string? GoogleOAuthUserId { get; set; }
    public bool HasGoogleOAuth => GoogleOAuthUserId != null;
    public bool HasPassword => PasswordHash != null;

    [NotMapped]
    public override string? PhoneNumber { get; set; }
    [NotMapped]
    public override bool PhoneNumberConfirmed { get; set; }

    // Navigation properties
    public virtual ICollection<Activity> ActivityList { get; set; } = new List<Activity>();
    public virtual ICollection<ActivityCategory> CategoryList { get; set; } = new List<ActivityCategory>();
    public virtual ICollection<ActivityRole> RoleList { get; set; } = new List<ActivityRole>();

    public virtual ICollection<ActivityHistory> ActivityHistoryList { get; set; } = new List<ActivityHistory>();
    public virtual ICollection<Alarm> AlarmList { get; set; } = new List<Alarm>();
    public virtual ICollection<WebExtensionData> WebExtensionDataList { get; set; } = new List<WebExtensionData>();

    public virtual ICollection<TodoList> TodoListColl { get; set; } = new List<TodoList>();
    public virtual ICollection<TaskUrgency> TaskUrgencyList { get; set; } = new List<TaskUrgency>();
    public virtual ICollection<PlannerTask> PlannerTaskList { get; set; } = new List<PlannerTask>();
    public virtual ICollection<RoutineTodoList> RoutineTodoListColl { get; set; } = new List<RoutineTodoList>();
    public virtual ICollection<RoutineTimePeriod> RoutineTimePeriodList { get; set; } = new List<RoutineTimePeriod>();
}