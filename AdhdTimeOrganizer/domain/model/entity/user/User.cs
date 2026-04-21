using System.ComponentModel.DataAnnotations.Schema;
using AdhdTimeOrganizer.domain.model.entity.activity;
using AdhdTimeOrganizer.domain.model.entity.activityHistory;
using AdhdTimeOrganizer.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.domain.model.entity.activityTracking;
using AdhdTimeOrganizer.domain.model.entity.activityTracking.android;
using AdhdTimeOrganizer.domain.model.entity.activityTracking.desktop;
using AdhdTimeOrganizer.domain.model.entity.todoList;
using AdhdTimeOrganizer.domain.model.entityInterface;
using AdhdTimeOrganizer.domain.model.@enum;
using Microsoft.AspNetCore.Identity;

namespace AdhdTimeOrganizer.domain.model.entity.user;

public sealed class User : IdentityUser<long>, IBaseTableEntity
{
    public DateTime CreatedTimestamp { get; set; }
    public DateTime ModifiedTimestamp { get; set; }
    public required AvailableLocales CurrentLocale { get; set; }
    public required TimeZoneInfo Timezone { get; set; }
    public string? GoogleOAuthUserId { get; set; }
    public string? GoogleCalendarRefreshToken { get; set; }
    public bool HasExtensionAccess { get; set; } = false;
    public bool HasGoogleOAuth => GoogleOAuthUserId != null;
    public bool HasPassword => PasswordHash != null;

    [NotMapped]
    public override string? PhoneNumber { get; set; }
    [NotMapped]
    public override bool PhoneNumberConfirmed { get; set; }

    // Navigation properties
    public ICollection<Calendar> Calendar { get; set; } = new List<Calendar>();
    public ICollection<Activity> ActivityList { get; set; } = new List<Activity>();
    public ICollection<ActivityCategory> CategoryList { get; set; } = new List<ActivityCategory>();
    public ICollection<ActivityRole> RoleList { get; set; } = new List<ActivityRole>();

    public ICollection<ActivityHistory> ActivityHistoryList { get; set; } = new List<ActivityHistory>();
    public ICollection<WebExtensionActivityEntry> WebExtensionActivityEntryList { get; set; } = new List<WebExtensionActivityEntry>();
    public ICollection<DesktopActivityEntry> DesktopActivityEntryList { get; set; } = new List<DesktopActivityEntry>();
    public ICollection<AndroidSessionData> AndroidSessionDataList { get; set; } = new List<AndroidSessionData>();

    public ICollection<TodoListItem> TodoListItemColl { get; set; } = new List<TodoListItem>();
    public ICollection<TodoList> TodoListColl { get; set; } = new List<TodoList>();
    public ICollection<TodoListCategory> TodoListCategoryColl { get; set; } = new List<TodoListCategory>();
    public ICollection<TaskPriority> TaskPriorityList { get; set; } = new List<TaskPriority>();
    public ICollection<PlannerTask> PlannerTaskList { get; set; } = new List<PlannerTask>();
    public ICollection<RoutineTodoList> RoutineTodoListColl { get; set; } = new List<RoutineTodoList>();
    public ICollection<RoutineTimePeriod> RoutineTimePeriodList { get; set; } = new List<RoutineTimePeriod>();
    public ICollection<RefreshToken> RefreshTokens { get; set; } = new List<RefreshToken>();


    public ICollection<TrackerDesktopMappingByPattern> TrackerDesktopMappingByPatternList { get; set; } = new List<TrackerDesktopMappingByPattern>();
    public ICollection<TrackerAndroidMappingByPattern> TrackerAndroidMappingByPatternList { get; set; } = new List<TrackerAndroidMappingByPattern>();


    public override string? Email
    {
        get => base.Email;
        set
        {
            base.Email = value;
            if (value != null)
            {
                UserName = value.Split(['@'], StringSplitOptions.None)[0];
            }
        }
    }
}