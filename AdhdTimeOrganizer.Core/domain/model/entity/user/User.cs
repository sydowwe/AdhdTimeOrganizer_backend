using AdhdTimeOrganizer.Core.domain.model.entity.activity;
using AdhdTimeOrganizer.Core.domain.model.entity.activity.memoryAnchor;
using Sydowwe.Framework.domain.audit;
using Sydowwe.Framework.domain.entity.user;
using Sydowwe.Framework.domain.entityInterface;

namespace AdhdTimeOrganizer.Core.domain.model.entity.user;

// IBaseTableEntity is satisfied entirely by the bases (Id from IdentityUser<long>, the timestamps from
// BaseUser); it is kept so the app's own BaseEntityConfigure/EnumColumn helpers still apply to this table.
public sealed class User : BaseUser, IBaseTableEntity
{
    [AuditIgnore]
    public string? GoogleOAuthUserId { get; set; }

    [AuditIgnore]
    public string? GoogleCalendarRefreshToken { get; set; }
    public bool HasExtensionAccess { get; set; } = false;
    public int FirstDayOfWeek { get; set; } = 1;


    public bool HasGoogleOAuth => GoogleOAuthUserId != null;

    // Navigation properties.
    //
    // Only the activity-area collections live here. Every other user-owned entity (planner tasks,
    // calendars, to-do lists, routines, reminders, history, tracking entries, tracker mappings,
    // planner settings) configures its own user FK from the dependent side via the parameterless
    // IsManyWithOneUser() / IsOneWithOneUser() — so User does NOT name those types.
    //
    // WHY: the inverse collections made User reference every feature area, which is a dependency
    // cycle the moment those areas become their own projects. Nothing in application code traversed
    // them; they existed only to hand the configuration helpers a navigation expression, and those
    // helpers take it optionally. Deleting one changes no column, index or cascade — the FK is still
    // configured, still required, still cascades. To query "this user's planner tasks", go through
    // the DbSet (dbContext.PlannerTasks.Where(t => t.UserId == ...)), which the global query filter
    // scopes for you anyway.
    public ICollection<Activity> ActivityList { get; set; } = new List<Activity>();
    public ICollection<ActivityCategory> CategoryList { get; set; } = new List<ActivityCategory>();
    public ICollection<ActivityRole> RoleList { get; set; } = new List<ActivityRole>();
    public ICollection<MemoryAnchor> MemoryAnchors { get; set; } = new List<MemoryAnchor>();

    // Framework's RefreshToken (Sydowwe.Framework.domain.entity.user) carries no User navigation, so
    // this collection is configured from the principal end in RefreshTokenConfiguration. It is the
    // only end of that relationship and therefore cannot be dropped.
    public ICollection<RefreshToken> RefreshTokens { get; set; } = new List<RefreshToken>();


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