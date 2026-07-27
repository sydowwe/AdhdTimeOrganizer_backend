using System.ComponentModel.DataAnnotations.Schema;
using AdhdTimeOrganizer.domain.model.entity.activity;
using AdhdTimeOrganizer.domain.model.entity.activity.memoryAnchor;
using AdhdTimeOrganizer.domain.model.entity.activityHistory;
using AdhdTimeOrganizer.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.domain.model.entity.activityTracking;
using AdhdTimeOrganizer.domain.model.entity.activityTracking.android;
using AdhdTimeOrganizer.domain.model.entity.activityTracking.desktop;
using AdhdTimeOrganizer.domain.model.entity.todoList;
using Sydowwe.Framework.domain.entity.user;
using Sydowwe.Framework.domain.entityInterface;

namespace AdhdTimeOrganizer.domain.model.entity.user;

// IBaseTableEntity is satisfied entirely by the bases (Id from IdentityUser<long>, the timestamps from
// BaseUser); it is kept so the app's own BaseEntityConfigure/EnumColumn helpers still apply to this table.
public sealed class User : BaseUser, IBaseTableEntity
{
    public string? GoogleOAuthUserId { get; set; }
    public string? GoogleCalendarRefreshToken { get; set; }
    public bool HasExtensionAccess { get; set; } = false;
    public int FirstDayOfWeek { get; set; } = 1;


    public bool HasGoogleOAuth => GoogleOAuthUserId != null;

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

    // Framework's RefreshToken (Sydowwe.Framework.domain.entity.user) carries no User navigation, so
    // this collection is configured from the principal end in RefreshTokenConfiguration.
    public ICollection<RefreshToken> RefreshTokens { get; set; } = new List<RefreshToken>();
    public UserPlannerSettings? PlannerSettings { get; set; }


    public ICollection<TrackerDesktopMappingByPattern> TrackerDesktopMappingByPatternList { get; set; } = new List<TrackerDesktopMappingByPattern>();
    public ICollection<TrackerAndroidMappingByPattern> TrackerAndroidMappingByPatternList { get; set; } = new List<TrackerAndroidMappingByPattern>();
    public ICollection<MemoryAnchor> MemoryAnchors { get; set; } = new List<MemoryAnchor>();


    public override string? Email
    {
        get => base.Email;
        set
        {
            base.Email = value;
            if (value != null)
                UserName = value;
        }
    }
}