using AdhdTimeOrganizer.Core.domain.model.entity.activity;
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

    /// <summary>
    /// Free text the user typed for "where I am", e.g. <c>Bratislava, SK</c> — the only input the leisure
    /// weather signal has. Null means the user has not set one, which every weather-dependent path must treat
    /// as "no opinion" rather than as an error.
    ///
    /// <para>Free text rather than a lat/lng pair on purpose: geocoding, the provider and the caching are the
    /// backend's business, and keeping the column a string means changing any of them needs no migration.</para>
    ///
    /// <para><b>Do not log this value.</b> A town is not a name or an address, but it is still personal data
    /// and log files survive a GDPR erasure — log <c>{UserId}</c> instead, as the weather endpoint does.</para>
    /// </summary>
    [AuditIgnore]
    public string? WeatherLocation { get; set; }

    /// <summary>
    /// The last time the user used the app (FO5), stamped at most once per <see cref="UserPresence.StampInterval"/>
    /// by the host's <c>LastActiveStampMiddleware</c> on any authenticated web-client request. Tracker tokens do not
    /// stamp it, because a desktop heartbeat is the user at their computer, not in the app.
    /// <para>
    /// Not the same as the framework's last <i>sign-in</i>: a session that stays signed in for a month signs in once.
    /// Null until the first request after the column was added. Every reader treats null as "present", since a
    /// user nobody has measured is not known to be away.
    /// </para>
    /// <para>
    /// Written only with <c>ExecuteUpdateAsync</c>, never through a tracked <see cref="User"/>, so it neither
    /// bumps the Identity concurrency stamp nor fails a preferences save that loaded the row a moment earlier.
    /// </para>
    /// </summary>
    [AuditIgnore]
    public DateTime? LastActiveAt { get; set; }

    /// <summary>
    /// The pending welcome-back card (FO5): where the absence began, i.e. the <see cref="LastActiveAt"/> the stamp
    /// replaced when it found a gap of at least <see cref="UserPresence.GreetAfter"/>. Set together with
    /// <see cref="WelcomeBackReturnedAt"/>, and cleared together with it once the user applies a fresh start or
    /// dismisses the card. Kept because <see cref="LastActiveAt"/> is overwritten by the very request that
    /// discovers the gap, so the card cannot be derived from it afterwards.
    /// </summary>
    [AuditIgnore]
    public DateTime? WelcomeBackAwayFrom { get; set; }

    /// <summary>When the user came back from the absence <see cref="WelcomeBackAwayFrom"/> began. The card is
    /// offered for <see cref="UserPresence.CardLifetime"/> after this, then lapses unanswered.</summary>
    [AuditIgnore]
    public DateTime? WelcomeBackReturnedAt { get; set; }


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