using System.Reflection;
using AdhdTimeOrganizer.domain.model.entity.user;
using AdhdTimeOrganizer.infrastructure.persistence;
using AdhdTimeOrganizer.Notifications.application;
using AdhdTimeOrganizer.Notifications.domain.entity;
using AdhdTimeOrganizer.Notifications.infrastructure;
using AdhdTimeOrganizer.Notifications.infrastructure.email;
using AdhdTimeOrganizer.Reminders.application.job;
using AdhdTimeOrganizer.Reminders.domain.entity;
using AdhdTimeOrganizer.Reminders.infrastructure.scheduling;
using AdhdTimeOrganizer.Scheduler.application.job;
using AdhdTimeOrganizer.Scheduler.domain.entity;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.Contracts.notification;
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