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

    /// <summary>
    /// FO10: when this user's day usually starts — the wake-up every calendar day of theirs is created with, and the
    /// one their untouched future days are rewritten to when it changes. See <see cref="UserSleepWindow"/>.
    /// </summary>
    public TimeOnly DefaultWakeUpTime { get; set; } = UserSleepWindow.Fallback.WakeUpTime;

    /// <inheritdoc cref="DefaultWakeUpTime"/>
    public TimeOnly DefaultBedTime { get; set; } = UserSleepWindow.Fallback.BedTime;

    public UserSleepWindow SleepWindow => new(DefaultWakeUpTime, DefaultBedTime);

    /// <summary>
    /// Whether the user gets the morning briefing — one notification at wake-up summing up today's plan, carry-overs,
    /// due and overdue to-dos and the routines suggested for today (the host's <c>MorningBriefingJobHandler</c>).
    /// On by default.
    /// <para>
    /// <b>In Core, not the host, because Routines reads it.</b> With the briefing on, the routine slice's own
    /// suggested-today digest would say the same thing a second time, so <c>RoutineSuggestedTodayJobHandler</c> skips
    /// every user who has this set; turning the briefing off brings that digest back. Routines can see Core but not
    /// the host, and a flag is the honest coupling — the host writing the routine sweep's marks to silence it would
    /// be a cross-slice side effect nobody would find.
    /// </para>
    /// </summary>
    public bool MorningBriefingEnabled { get; set; } = true;

    /// <summary>
    /// How long after the day's wake-up the briefing goes out, in minutes (0 – <see cref="MorningBriefingMaxOffsetMinutes"/>).
    /// 0 is at wake-up itself.
    /// </summary>
    public int MorningBriefingOffsetMinutes { get; set; }

    /// <summary>The longest offset a user may choose: three hours — past that it is no longer the morning's message.</summary>
    public const int MorningBriefingMaxOffsetMinutes = 180;

    /// <summary>
    /// The user-local date whose morning briefing has been decided: sent, found to have nothing to say, or stood in
    /// for by the welcome-back card. What stops the next quarter-hour run from deciding the same morning again. Only a
    /// dispatch that failed leaves the day open, so a later run that morning retries. Written with
    /// <c>ExecuteUpdateAsync</c> only, like <see cref="LastActiveAt"/>.
    /// </summary>
    [AuditIgnore]
    public DateOnly? MorningBriefingHandledFor { get; set; }

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