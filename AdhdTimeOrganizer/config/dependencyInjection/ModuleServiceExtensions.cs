using System.Reflection;
using AdhdTimeOrganizer.ActivityProfiles.domain.model.entity;
using AdhdTimeOrganizer.application.service.welcomeBack;
using AdhdTimeOrganizer.Core.domain.model.entity.activity;
using AdhdTimeOrganizer.Core.domain.model.entity.user;
using AdhdTimeOrganizer.History.domain.model.entity.activityHistory;
using AdhdTimeOrganizer.infrastructure.notification;
using AdhdTimeOrganizer.infrastructure.persistence;
using AdhdTimeOrganizer.Planning.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.Routines.domain.model.entity.todoList;
using AdhdTimeOrganizer.TodoLists.domain.model.entity.todoList;
using AdhdTimeOrganizer.Tracking.domain.model.entity.activityTracking.desktop;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Sydowwe.Framework.Contracts.notification;
using Sydowwe.Framework.Contracts.notification.action;
using Sydowwe.Framework.Contracts.user;
using Sydowwe.Framework.domain.@enum;
using Sydowwe.Notifications.application;
using Sydowwe.Notifications.application.text;
using Sydowwe.Notifications.domain.entity;
using Sydowwe.Notifications.infrastructure;
using Sydowwe.Notifications.infrastructure.email;
using Sydowwe.Reminders.application.job;
using Sydowwe.Reminders.domain.entity;
using Sydowwe.Reminders.infrastructure.scheduling;
using Sydowwe.Scheduler.application.job;
using Sydowwe.Scheduler.domain.entity;
using Sydowwe.Scheduler.Xlsx;
using FrameworkDi = Sydowwe.Framework.config.dependencyInjection;

namespace AdhdTimeOrganizer.config.dependencyInjection;

/// <summary>
/// Registers the services of the Notifications / Reminders / Scheduler modules.
/// <para>
/// The assembly list is explicit rather than <c>AppDomain.CurrentDomain.GetAssemblies()</c>: the CLR loads
/// an assembly lazily on first type use, so a module whose types have not been touched yet would silently
/// contribute nothing to a scan taken at startup. That reliability is why this scan exists at all.
/// </para>
/// <para>
/// <b>This is the only scan that may cover these assemblies.</b> The modules mark their services with the
/// same <c>Sydowwe.Framework</c> lifetime interfaces
/// <see cref="DependencyInjectionExtensions.AddDependencyInjection"/> scans for, so once the CLR has loaded
/// them they also show up in its <c>AppDomain</c> sweep — registering every module service a second time.
/// Single resolutions survive that (last wins), but anything resolved as <c>IEnumerable&lt;T&gt;</c>
/// silently doubles: two <c>ReminderScanJobHandler</c>s means the dispatch scan runs twice per fire, and
/// two of each seeder means every seeder runs twice. <see cref="ModuleAssemblies"/> is therefore excluded
/// from that sweep — keep the two lists joined rather than duplicating the exclusion.
/// </para>
/// </summary>
public static class ModuleServiceExtensions
{
    /// <summary>
    /// The module assemblies this extension owns the scanning of. Read by
    /// <see cref="DependencyInjectionExtensions"/> to exclude them from its <c>AppDomain</c> sweep.
    /// </summary>
    internal static readonly Assembly[] ModuleAssemblies =
    [
        // AdhdTimeOrganizer.Core — the first vertical-slice project. It is not a "module" in the
        // Notifications/Reminders/Scheduler sense, but it is scanned the same way and for the same
        // reason: its seeders and services carry the framework lifetime markers, so it must be scanned
        // exactly once. Listing it here (and therefore excluding it from the AppDomain sweep) is what
        // guarantees that — every per-user default seeder in it would otherwise run twice.
        typeof(Activity).Assembly,
        // AdhdTimeOrganizer.TodoLists — same reasoning as Core: TaskPrioritySeeder and TodoListSeeder
        // carry lifetime markers, so being in this list (and out of the AppDomain sweep) is what keeps
        // them registered exactly once.
        typeof(TodoList).Assembly,
        // AdhdTimeOrganizer.History — same reasoning again: ActivityHistorySeeder and
        // ActivityHistoryTimeAttributionSink carry lifetime markers. Note History registers no
        // IActivityMembershipSource of its own; it only consumes them. The sink is the other direction:
        // History is the sole implementer of Core's IActivityTimeAttributionSink, and Tracking's
        // heartbeat is its only consumer. Drop this entry and the heartbeat 500s on activation — which
        // is the intended failure, not a silent one; see the interface's remarks.
        typeof(ActivityHistory).Assembly,
        // AdhdTimeOrganizer.Routines — same reasoning: RoutineTimePeriodSeeder / RoutineTodoListSeeder
        // and RoutineTodoListActivityMembershipSource carry lifetime markers.
        typeof(RoutineTimePeriod).Assembly,
        // AdhdTimeOrganizer.Planning — same reasoning: CalendarSeeder, TaskImportanceSeeder,
        // UserPlannerSettingsSeeder and the two dev template seeders carry lifetime markers, as does
        // ReminderRegistrationService. Being in this list (and therefore out of the AppDomain sweep)
        // is what keeps each of them registered exactly once.
        typeof(PlannerTask).Assembly,
        // AdhdTimeOrganizer.Tracking — same reasoning: WebExtensionDataSeeder carries a lifetime
        // marker, so being in this list (and therefore out of the AppDomain sweep) is what keeps it
        // registered exactly once. It is a dev seeder that truncates web_extension_activity_entry, so a
        // double registration would truncate and reseed twice per run.
        typeof(DesktopActivityEntry).Assembly,
        // AdhdTimeOrganizer.ActivityProfiles — same reasoning, and this slice has the most seeders of
        // any: four per-user default seeders (the activity lookups) plus eight dev seeders. The dev
        // ones truncate, so a double registration would truncate and reseed each table twice per run,
        // and the per-user defaults would insert every default twice on sign-up.
        typeof(ActivityBacklogProfile).Assembly,
        typeof(Notification).Assembly,
        typeof(ReminderDefinition).Assembly,
        typeof(ScheduledJob).Assembly
    ];

    public static IServiceCollection AddModuleServices(this IServiceCollection services, IConfiguration configuration)
    {
        // 34 module services take a plain `DbContext` rather than a concrete one — that is how they stay
        // host-agnostic. AddDbContext<AppDbContext> only registers the concrete type, so without this alias
        // every single one of them fails to activate.
        services.AddScoped<DbContext>(sp => sp.GetRequiredService<AppDbContext>());
        services.TryAddSingleton<TimeProvider>(TimeProvider.System);

        // The Notifications module pushes live updates through NotificationHub.
        services.AddSignalR();

        services.Scan(scan => scan
            .FromAssemblies(ModuleAssemblies)
            .AddClasses(classes => classes.AssignableTo<FrameworkDi.IScopedService>())
            .AsImplementedInterfaces()
            .WithScopedLifetime());

        services.Scan(scan => scan
            .FromAssemblies(ModuleAssemblies)
            .AddClasses(classes => classes.AssignableTo<FrameworkDi.ISingletonService>())
            .AsImplementedInterfaces()
            .WithSingletonLifetime());

        services.Scan(scan => scan
            .FromAssemblies(ModuleAssemblies)
            .AddClasses(classes => classes.AssignableTo<FrameworkDi.ITransientService>())
            .AsImplementedInterfaces()
            .WithTransientLifetime());

        // NotificationService is generic over the host's user type (it needs UserManager<TUser> to resolve
        // recipient emails) and carries no lifetime marker, so the scans above cannot see it. Close it over
        // User here or INotificationService fails to resolve the moment anything sends a notification.
        services.AddScoped<INotificationService, NotificationService<User>>();
        services.AddScoped<IDeferredNotificationDispatcher>(sp =>
            (NotificationService<User>)sp.GetRequiredService<INotificationService>());

        // Same story, same trap: generic over the host's user type (Timezone lives on BaseUser), so no marker
        // scan can see it. Quiet hours are evaluated in each recipient's OWN zone and this is where that zone
        // comes from — miss this line and QuietHoursReader silently falls back to UTC for everybody, which is
        // not an error anywhere, just a window enforced an hour or two off the one the settings page shows.
        // ModuleWiringTests pins it.
        services.AddScoped<IUserTimeZoneReader, UserTimeZoneReader<User>>();

        // And the language each notification is rendered in (B15) — same generic-over-User shape, same reason
        // no scan can see it. This one at least fails loudly: NotificationService and the bell endpoint take
        // it as a constructor dependency, so a missing line is a resolution error on the first send or read.
        services.AddScoped<IUserLocaleReader, UserLocaleReader<User>>();

        // FO5's back-off: who is away from the app, so the routine nudges in HeldWhileAwayTypes wait in the bell
        // instead of reaching their phone. The dispatcher takes it as an OPTIONAL dependency, so a missing line
        // here is silent: nobody is ever away and the nudges keep coming. ModuleWiringTests pins it.
        services.AddScoped<IRecipientEngagementReader, RecipientEngagementReader>();

        // The only INotificationPayloadEnricher in the solution, and it carries no lifetime marker (it is
        // meant as the host-supplied default). Swap for a real enricher if payloads ever need overlays.
        services.AddScoped<INotificationPayloadEnricher, NoOpNotificationPayloadEnricher>();

        // Scheduler dashboard XLSX export. Registered by name, not by marker scan: Sydowwe.Scheduler.Xlsx is
        // deliberately absent from ModuleAssemblies (adding it there to pick up a marker would put it in the
        // path of the double-registration trap documented above). Without this call the dashboard still
        // exports CSV and XLSX requests throw — see IXlsxTableRenderer.
        services.AddSchedulerXlsxExport();

        AddModuleOptions(services, configuration);

        return services;
    }

    /// <summary>
    /// Binds each module's options section. Unbound <c>IOptions&lt;T&gt;</c> still resolves, so a missing
    /// binding here fails silently as all-default settings rather than as a startup error — bind them
    /// explicitly so appsettings actually takes effect.
    /// </summary>
    private static void AddModuleOptions(IServiceCollection services, IConfiguration configuration)
    {
        AddNotificationCatalog(services);
        AddNotificationVocabulary(services);

        services.Configure<PushNotificationOptions>(configuration.GetSection(PushNotificationOptions.SectionName));
        services.Configure<EmailNotificationOptions>(configuration.GetSection(EmailNotificationOptions.SectionName));
        services.Configure<ReminderRetentionOptions>(configuration.GetSection(ReminderRetentionOptions.SectionName));
        services.Configure<ReminderDigestOptions>(configuration.GetSection(ReminderDigestOptions.SectionName));
        services.Configure<ReminderScanOptions>(configuration.GetSection(ReminderScanOptions.SectionName));
        services.Configure<OverdueJobSweepOptions>(configuration.GetSection(OverdueJobSweepOptions.SectionName));
        services.Configure<SchedulerRetentionOptions>(configuration.GetSection(SchedulerRetentionOptions.SectionName));
    }

    /// <summary>
    /// The notification kinds <b>this</b> portal can raise — the nine of <see cref="NotificationType"/>'s
    /// twenty that some producer here actually calls <c>NotifyAsync</c> with.
    /// <para>
    /// Declared in code rather than bound from configuration on purpose: which events an app produces is a
    /// property of its code, not of a deployment's settings, and an appsettings typo would silently drop a
    /// row from the settings screen. Nine of the eleven kinds left out belong to the HR/inventory product
    /// that shares <c>Sydowwe.Framework.Contracts</c> — there is no inventory and no leave here, so a
    /// preference row for <c>StockLow</c> asks the user a question this portal can never act on, in an enum
    /// name no Slovak locale here translates. The other two are the Reminders module's own
    /// <c>DeadlineApproaching</c> and <c>ReminderDigest</c>, which nothing here raises (see the list below).
    /// </para>
    /// <para>
    /// <b>Keep this list next to the producer when you add a kind.</b> Nothing fails if you forget: the kind
    /// still dispatches, it simply loses its row on <c>/nastavenia/pripomienky</c> and becomes unmutable
    /// through the UI. <c>NotificationCatalogTests</c> is what turns that into a red test — it asserts the
    /// catalog covers every quiet-hours-exempt kind and that the settings matrix is exactly this list.
    /// </para>
    /// </summary>
    private static void AddNotificationCatalog(IServiceCollection services) =>
        services.Configure<NotificationCatalogOptions>(o =>
        {
            // Dev/QA probe: only an administrator can raise it (SendTestNotificationEndpoint), so only an
            // administrator is offered a switch for it.
            o.AdminOnlyTypes = [NotificationType.Test];

            // FO5's back-off: for a user away three days or more (RecipientEngagementReader), these wait in the
            // bell and are not pushed or mailed. Only the unsolicited ones about a day, period or block that passes
            // without them: the routine sweeps' nudges and the planner's automatic block reminders. The reminders
            // the user wrote, the to-do due reminders, the timer alarm and the scheduler's alerts are delivered
            // whether anyone is around or not.
            o.HeldWhileAwayTypes =
            [
                NotificationType.RoutinePeriodEndingSoon,
                NotificationType.RoutinePeriodEnded,
                NotificationType.RoutineStreakGraceExpiring,
                NotificationType.RoutineSuggestedToday,

                // Planning's automatic block reminders: system-generated, about a moment that passes without them,
                // and a whole day of them is exactly what a returning user should not find on their lock screen.
                // The reminders the user wrote (PersonalReminder) are not held — they asked for those.
                NotificationType.PlannedBlockStarting,

                // Planning's slip sweep (audit N5): "blocks slipped" and "time's up" are about a moment, and a
                // returning user does not need a pile of them on the lock screen.
                NotificationType.PlannedBlocksMissed,
                NotificationType.PlannedBlockOverrun,

                // The host's morning briefing: unsolicited and about a morning that passes without it. A returning
                // user gets the welcome-back card instead (MorningBriefingJobHandler skips them while it is open).
                NotificationType.MorningBriefing
            ];

            // Bell-only until the user ticks push: low-urgency kinds worth finding the next time the app is open,
            // not worth a buzz. Every kind raised today is something the user asked for or about a moment, so the
            // list starts empty; a new bell-only kind goes here together with its producer.
            o.PushOffByDefault = [];

            // P2-03: what the lock screen lets the user do without opening the app — every extra step between
            // "reminded" and "handled" is a place an ADHD user drops the task. Two buttons at most (what Chrome and
            // Android show). Snooze is the Reminders module's and appears only on a push that delivered a reminder
            // occurrence; Dismiss is the Notifications module's (mark read); the rest are the owning slice's
            // INotificationActionHandler. NotificationCatalogTests holds the second copy of this table.
            o.Actions = new()
            {
                [NotificationType.PersonalReminder] = [NotificationAction.Snooze(10), NotificationAction.Dismiss],
                [NotificationType.TodoItemDue] = [NotificationAction.Done, NotificationAction.Snooze(60)],
                [NotificationType.PlannedBlockStarting] = [NotificationAction.Start, NotificationAction.Skip],
                [NotificationType.PlannedBlockOverrun] = [NotificationAction.Done, NotificationAction.Extend(15)]
            };

            o.SupportedTypes =
        [
            // Reminders module: the user's own self-set nudge. Its generic DeadlineApproaching and batched
            // ReminderDigest are deliberately absent — Planning registers PersonalReminder with no DigestKey,
            // so neither is ever sent here, and their rows muted nothing (DeadlineApproaching even showed
            // E-mail ✓, a promise the app never kept). Each comes back together with a producer.
            NotificationType.PersonalReminder,

            // Routines slice (RoutinePeriodNotificationService).
            NotificationType.RoutinePeriodEndingSoon,
            NotificationType.RoutinePeriodEnded,
            NotificationType.RoutineStreakGraceExpiring,
            NotificationType.RoutineSuggestedToday,

            // Scheduler module: the failure notifier and the overdue sweep.
            NotificationType.ScheduledJobFailed,
            NotificationType.ScheduledJobOverdue,

            // TodoLists slice: TodoListItemDueReminderSync, registered through the Reminders module per timed item.
            // Not held while away: it is about the user's own commitment, not a nudge to come back.
            NotificationType.TodoItemDue,

            // Planning slice: the automatic reminder PlannedBlockAutoReminders creates for a new block, under the
            // user's own UserPlannerSettings.AutoReminderMode. Its own row, so muting it leaves PersonalReminder alone.
            NotificationType.PlannedBlockStarting,

            // Planning slice: PlannerSlipNudgeJobHandler, through PlannerNotificationService.
            NotificationType.PlannedBlocksMissed,
            NotificationType.PlannedBlockOverrun,

            // The host: MorningBriefingJobHandler, through MorningBriefingNotifier. Its own row, so the briefing's
            // channels can be chosen apart from the routine digest it replaces.
            NotificationType.MorningBriefing,

            // History slice: TimerBoundaryAlarmJobHandler, on every pomodoro boundary.
            NotificationType.TimerBoundary,

            // History slice: the same handler, for a running stopwatch's check-ins (P3-03). Not held while away — the
            // user started this stopwatch — and not quiet-hours exempt: the handler skips one that lands inside the
            // window rather than letting it be deferred. Push on, email off, no action buttons: Stop is client state,
            // so the tap opens the stopwatch page instead.
            NotificationType.StopwatchCheckIn,

            // Tracking slice: TrackerSilenceJobHandler, through TrackerSilenceNotifier (P2-04). Not held while away —
            // the rule only fires for a user the app saw within a day, and it is operational, not a nudge. Push on,
            // email off (NotificationChannelDefaults): the user should hear about it, but it is not worth a mail.
            NotificationType.TrackerSilent,

            // Raised on demand by SendTestNotificationEndpoint (admin-only, see AdminOnlyTypes above).
            NotificationType.Test
        ];
        });

    /// <summary>
    /// The words for the kinds in the catalog above (B15). The framework's tables word only the kinds its own
    /// modules raise — a kind this portal raises (the routine nudges, the pomodoro boundary, the personal
    /// reminder) renders the generic "Nová notifikácia" out of them, so the portal's subclasses are registered
    /// here, one per language. <c>Cz</c> is pointed at the Slovak table on purpose: the module's own fallback
    /// for it is the module's Slovak, which lacks this portal's kinds, and the client ships no Czech either.
    /// <c>NotificationVocabularyTests</c> renders every catalogued kind in every locale and refuses the fallback.
    /// </summary>
    private static void AddNotificationVocabulary(IServiceCollection services)
    {
        var sk = new SkPortalNotificationTexts();
        services.Configure<NotificationTextOptions>(o => o
            .Use(AvailableLocales.Sk, sk)
            .Use(AvailableLocales.Cz, sk)
            .Use(AvailableLocales.En, new EnPortalNotificationTexts()));
    }
}
