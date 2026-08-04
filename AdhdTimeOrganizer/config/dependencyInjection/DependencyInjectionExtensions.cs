using System.Reflection;
using AdhdTimeOrganizer.domain.model.entity.user;
using Sydowwe.Framework.config.dependencyInjection;
using Sydowwe.Framework.domain.extServiceContract.user;
using Sydowwe.Framework.domain.extServiceContract.user.auth;
using Sydowwe.Framework.infrastructure.extService.user;
using Sydowwe.Framework.infrastructure.extService.user.auth;

namespace AdhdTimeOrganizer.config.dependencyInjection;

public static class DependencyInjectionExtensions
{
    /// <summary>
    /// The assemblies the marker-interface scans below run over.
    /// <para>
    /// <c>AppDomain.CurrentDomain.GetAssemblies()</c> returns what the CLR has <b>already loaded</b>,
    /// and loading is lazy — an assembly whose types nothing has touched yet contributes nothing to a
    /// scan taken at startup. Sydowwe.Framework is therefore pinned by a <c>typeof</c>, which both
    /// forces the load and guarantees its services (the seeder managers among them) are seen.
    /// <c>Distinct</c> keeps it from being scanned twice — Scrutor would otherwise register every
    /// framework service twice, and anything resolved as <c>IEnumerable&lt;T&gt;</c> would run twice.
    /// </para>
    /// <para>
    /// The Notifications / Reminders / Scheduler assemblies are excluded for that same reason:
    /// <see cref="ModuleServiceExtensions"/> scans them from an explicit list (lazy loading makes their
    /// presence here a coin flip), and they mark their services with these very same interfaces — so
    /// whenever the CLR had loaded them by this point, both scans registered every module service and every
    /// <c>IEnumerable&lt;T&gt;</c> consumer got two of each. <c>ReminderScanJobHandler</c> was the visible
    /// casualty: two registrations, so the reminder dispatch scan ran twice on every fire.
    /// </para>
    /// </summary>
    private static Assembly[] ScannedAssemblies =>
        AppDomain.CurrentDomain.GetAssemblies()
            .Append(typeof(IScopedService).Assembly)
            .Distinct()
            .Except(ModuleServiceExtensions.ModuleAssemblies)
            .ToArray();

    public static IServiceCollection AddDependencyInjection(this IServiceCollection services)
    {
        var assemblies = ScannedAssemblies;

        services.Scan(scan => scan
            .FromAssemblies(assemblies)
            .AddClasses(classes => classes.AssignableTo<IScopedService>())
            .AsImplementedInterfaces()
            .WithScopedLifetime());

        services.Scan(scan => scan
            .FromAssemblies(assemblies)
            .AddClasses(classes => classes.AssignableTo<ISingletonService>())
            .AsImplementedInterfaces()
            .WithSingletonLifetime());

        services.Scan(scan => scan
            .FromAssemblies(assemblies)
            .AddClasses(classes => classes.AssignableTo<ITransientService>())
            .AsImplementedInterfaces()
            .WithTransientLifetime());

        services.Scan(scan => scan
            .FromAssemblies(assemblies)
            .AddClasses(classes => classes.AssignableTo<IMapperService>())
            .AsSelf()
            .WithSingletonLifetime());

        services.AddHttpClient<IGoogleRecaptchaService, GoogleRecaptchaService>();

        // The framework's user services are open generics over TUser, so the marker-interface scans above
        // can't close them — bind the concrete User here, same as ModuleServiceExtensions does.
        // Missing one of these compiles fine and fails at runtime on first resolution.
        services.AddScoped<ITwoFactorAuthService<User>, TwoFactorAuthService<User>>();
        services.AddSingleton<IUserEmailSenderService<User>, UserEmailSenderService<User>>();

        // JwtService implements both the generic surface (login flows, typed user) and the non-generic
        // one (refresh / cookie helpers). Register the concrete type once and forward both interfaces to
        // it so a single request shares one instance.
        services.AddScoped<JwtService<User>>();
        services.AddScoped<IJwtService<User>>(sp => sp.GetRequiredService<JwtService<User>>());
        services.AddScoped<IJwtService>(sp => sp.GetRequiredService<JwtService<User>>());

        // IRefreshTokenService is NOT listed here on purpose: framework's RefreshTokenService is
        // non-generic and carries IScopedService, so the scan above picks it up. It takes a plain
        // DbContext, aliased to AppDbContext in ModuleServiceExtensions.

        return services;
    }
}