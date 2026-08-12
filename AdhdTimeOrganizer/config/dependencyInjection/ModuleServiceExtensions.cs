using System.Reflection;
using AdhdTimeOrganizer.ActivityProfiles.domain.model.entity;
using AdhdTimeOrganizer.Core.domain.model.entity.activity;
using AdhdTimeOrganizer.Core.domain.model.entity.user;
using AdhdTimeOrganizer.History.domain.model.entity.activityHistory;
using AdhdTimeOrganizer.infrastructure.persistence;
using AdhdTimeOrganizer.Planning.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.Routines.domain.model.entity.todoList;
using AdhdTimeOrganizer.TodoLists.domain.model.entity.todoList;
using AdhdTimeOrganizer.Tracking.domain.model.entity.activityTracking.desktop;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.Contracts.notification;
using Sydowwe.Notifications.application;
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
        services.Configure<PushNotificationOptions>(configuration.GetSection(PushNotificationOptions.SectionName));
        services.Configure<EmailNotificationOptions>(configuration.GetSection(EmailNotificationOptions.SectionName));
        services.Configure<ReminderRetentionOptions>(configuration.GetSection(ReminderRetentionOptions.SectionName));
        services.Configure<ReminderDigestOptions>(configuration.GetSection(ReminderDigestOptions.SectionName));
        services.Configure<ReminderScanOptions>(configuration.GetSection(ReminderScanOptions.SectionName));
        services.Configure<OverdueJobSweepOptions>(configuration.GetSection(OverdueJobSweepOptions.SectionName));
        services.Configure<SchedulerRetentionOptions>(configuration.GetSection(SchedulerRetentionOptions.SectionName));
    }

}