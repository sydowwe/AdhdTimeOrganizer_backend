using System.ComponentModel.DataAnnotations.Schema;
using AdhdTimeOrganizer.Command.domain.model.entity.activity;
using AdhdTimeOrganizer.Command.domain.model.entity.activityHistory;
using AdhdTimeOrganizer.Command.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.Common.domain.model.entityInterface;
using AdhdTimeOrganizer.Common.domain.model.@enum;
using Microsoft.AspNetCore.Identity;

namespace AdhdTimeOrganizer.Command.domain.model.entity.user;

public class UserEntity : IdentityUser<long>, IEntity
{
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
    public virtual ICollection<Category> CategoryList { get; set; } = new List<Category>();
    public virtual ICollection<Role> RoleList { get; set; } = new List<Role>();

    public virtual ICollection<ActivityHistory> ActivityHistoryList { get; set; } = new List<ActivityHistory>();
    public virtual ICollection<Alarm> AlarmList { get; set; } = new List<Alarm>();
    public virtual ICollection<WebExtensionData> WebExtensionDataList { get; set; } = new List<WebExtensionData>();

    public virtual ICollection<ToDoList> ToDoListColl { get; set; } = new List<ToDoList>();
    public virtual ICollection<TaskUrgency> TaskUrgencyList { get; set; } = new List<TaskUrgency>();
    public virtual ICollection<PlannerTask> PlannerTaskList { get; set; } = new List<PlannerTask>();
    public virtual ICollection<RoutineToDoList> RoutineToDoListColl { get; set; } = new List<RoutineToDoList>();
    public virtual ICollection<RoutineTimePeriod> RoutineTimePeriodList { get; set; } = new List<RoutineTimePeriod>();
}