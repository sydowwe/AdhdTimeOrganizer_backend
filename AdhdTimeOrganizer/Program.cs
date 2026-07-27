using System.Globalization;
using System.Net;
using System.Text.Json.Serialization;
using AdhdTimeOrganizer.application.endpoint.@base;
using AdhdTimeOrganizer.config;
using AdhdTimeOrganizer.config.dependencyInjection;
using AdhdTimeOrganizer.infrastructure.extService;
using AdhdTimeOrganizer.infrastructure.jobs;
using AdhdTimeOrganizer.infrastructure.persistence;
using AdhdTimeOrganizer.infrastructure.persistence.interceptors;
using AdhdTimeOrganizer.infrastructure.security;
using AdhdTimeOrganizer.infrastructure.settings;
using AdhdTimeOrganizer.Notifications.domain.entity;
using AdhdTimeOrganizer.Notifications.infrastructure.realtime;
using AdhdTimeOrganizer.Notifications.infrastructure.scheduling;
using AdhdTimeOrganizer.Reminders.domain.entity;
using AdhdTimeOrganizer.Reminders.infrastructure.scheduling;
using AdhdTimeOrganizer.Scheduler.domain.entity;
using AdhdTimeOrganizer.Scheduler.infrastructure;
using AdhdTimeOrganizer.Scheduler.infrastructure.scheduling;
using DotNetEnv;
using FastEndpoints;
using FastEndpoints.Swagger;
using Microsoft.AspNetCore.CookiePolicy;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Quartz;
using Serilog;
using Serilog.Events;
using Sydowwe.Framework.application.middleware;
using Sydowwe.Framework.config;
using Sydowwe.Framework.domain.helper;
using Sydowwe.Framework.infrastructure.extService.user.auth;
using Sydowwe.Framework.infrastructure.persistence;
using Sydowwe.Framework.infrastructure.security;
using Sydowwe.Framework.infrastructure.persistence.seeder.@interface.manager;

try
{
    // Load environment variables
    Env.Load();

    // Set default culture
    var defaultCulture = new CultureInfo("sk-SK");
    CultureInfo.DefaultThreadCurrentCulture = defaultCulture;
    CultureInfo.DefaultThreadCurrentUICulture = defaultCulture;

    var builder = WebApplication.CreateBuilder(args);

    // Configure configuration sources
    builder.Configuration
        .SetBasePath(AppContext.BaseDirectory)
        .AddJsonFile("appsettings.json", false, true)
        .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", true, true);

    // Configure Serilog
    builder.Logging.ClearProviders();
    SerilogConfig.ConfigureSerilog(builder.Configuration, builder.Host, DatabaseStringsHelper.GetLogDatabaseConnectionString);

    // Configure services
    ConfigureServices(builder.Configuration, builder.Services, builder.Environment.IsDevelopment());
    LoadSettingsFromConfiguration(builder.Configuration, builder.Services);

    var app = builder.Build();
    var logger = app.Services.GetRequiredService<ILogger<AdhdTimeOrganizer.Program>>();

    logger.LogInformation("Backend starting.");

    // Configure the HTTP request pipeline
    ConfigurePipeline(app, logger);

    // Database seeding
    await SeedDatabase(app.Services, builder.Environment.IsDevelopment(), logger);


    logger.LogInformation("Backend started.");
    await app.RunAsync();
}
catch (Exception ex)
{
    Console.WriteLine("Host terminated unexpectedly: \n " + ex.Message);
    Log.Fatal(ex, "Host terminated unexpectedly");
    throw;
}
finally
{
    await Log.CloseAndFlushAsync();
}

return;

static void ConfigureServices(IConfiguration configuration, IServiceCollection services, bool isDevelopment)
{
    // HTTP context accessor
    services.AddHttpContextAccessor();

    // Interceptors
    services.AddScoped<SuggestionPatternRefreshInterceptor>();

    // Database configuration
    services.AddDbContext<AppDbContext>((sp, options) =>
        options.UseNpgsql(DatabaseStringsHelper.GetDefaultDatabaseConnectionString, b => b.MigrationsAssembly(typeof(AdhdTimeOrganizer.Program).Assembly.FullName))
            .UseSnakeCaseNamingConvention()
            .ReplaceService<IMigrationsSqlGenerator, PartitionedNpgsqlMigrationsSqlGenerator>()
            .AddInterceptors(sp.GetRequiredService<SuggestionPatternRefreshInterceptor>())
            .LogTo(Console.WriteLine));

    // Dependency injection
    services.AddDependencyInjection();
    services.AddModuleServices(configuration);

    // Identity services

    // FastEndpoints
    // Restrict discovery to this assembly -- without it, any other FastEndpoints-using assembly loaded
    // into the same process (e.g. Sydowwe.Framework, pulled in transitively by the integration test host)
    // gets its endpoints registered too, and they fail to activate since their DI dependencies were never
    // wired up here.
    services.AddFastEndpoints(o =>
    {
        o.DisableAutoDiscovery = true;
        o.Assemblies =
        [
            typeof(Program).Assembly,
            typeof(Notification).Assembly,
            typeof(ReminderDefinition).Assembly,
            typeof(ScheduledJob).Assembly
        ];
    });
    services.AddSingleton<IGlobalPostProcessor, ErrorLoggingPostProcessor>();
    if (isDevelopment)
        services.SwaggerDocument();

    services.AddIdentityServices();

    // Background services
    services.AddHostedService<RefreshTokenCleanupService>();

    services.AddQuartz(q =>
    {
        // Registers the Scheduler module's generic dispatcher job that every IScheduler-registered
        // recurring trigger points at. Must be inside the host's single AddQuartz call.
        q.AddSchedulerQuartzDefaults();

        q.AddJob<RoutineTodoListResetJob>(opts =>
            opts.WithIdentity("routine-reset", "routine"));

        q.AddTrigger(opts => opts
            .ForJob("routine-reset", "routine")
            .WithIdentity("routine-reset-trigger", "routine")
            .WithCronSchedule("0 0 2 * * ?")); // 2:00 AM daily
    });
    services.AddQuartzHostedService(SchedulerQuartzConfig.ConfigureSchedulerHostedService);

    // Owner-side boot reconciliation: each module pushes its recurring-job registrations to the Scheduler
    // via Kernel's IScheduler. Idempotent upserts by JobKey, and required on every boot because the
    // Quartz RAM job store loses all triggers on restart.
    services.AddHostedService<NotificationsScheduledJobsRegistrar>();
    services.AddHostedService<RemindersScheduledJobsRegistrar>();
    services.AddHostedService<SchedulerScheduledJobsRegistrar>();

    // Caching
    services.AddDistributedMemoryCache();

    // Cookie policy configuration
    services.Configure<CookiePolicyOptions>(options =>
    {
        options.MinimumSameSitePolicy = SameSiteMode.Strict;
        options.HttpOnly = HttpOnlyPolicy.Always;
        options.Secure = CookieSecurePolicy.Always;
    });

    // Session configuration
    services.AddSession(options =>
    {
        options.Cookie.HttpOnly = true;
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        options.Cookie.SameSite = SameSiteMode.Strict;
        options.Cookie.Name = "__Host-SessionId";
        options.IdleTimeout = TimeSpan.FromMinutes(30);
        options.Cookie.IsEssential = true;
    });

    // JSON serialization configuration for FastEndpoints
    services.ConfigureHttpJsonOptions(options =>
    {
        options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
        options.SerializerOptions.Converters.Add(new DateOnlyJsonConverter());
    });
    // File upload configuration
    services.Configure<KestrelServerOptions>(options => { options.Limits.MaxRequestBodySize = configuration.GetValue<int>("FileUpload:MaxFileSizeInMB") * 1024 * 1024; });

    services.Configure<FormOptions>(options => { options.MultipartBodyLengthLimit = configuration.GetValue<int>("FileUpload:MaxFileSizeInMB") * 1024 * 1024; });

// CORS configuration
    services.AddCors(options =>
    {
        var pageUrl = Helper.GetEnvVar("PAGE_URL");
        var extensionId = Helper.GetEnvVar("EXTENSION_ID"); // Chrome extension ID
        options.AddPolicy("AllowFrontend", corsBuilder =>
        {
            var origins = new List<string> { "https://localhost:3000", "https://localhost:5173", pageUrl };

            // Add Chrome extension origin if configured
            if (!string.IsNullOrEmpty(extensionId))
                origins.Add($"chrome-extension://{extensionId}");

            corsBuilder.WithOrigins(origins.ToArray())
                .AllowAnyHeader()
                .AllowAnyMethod()
                .AllowCredentials()
                .SetIsOriginAllowedToAllowWildcardSubdomains();
        });
    });


    // Localization
    services.AddLocalization(options => options.ResourcesPath = "Resources");

    services.Configure<RequestLocalizationOptions>(options =>
    {
        var supportedCultures = new[] { "sk-SK", "en-US" };
        var defaultCulture = new CultureInfo("sk-SK");

        options.AddSupportedCultures(supportedCultures)
            .AddSupportedUICultures(supportedCultures);
        options.DefaultRequestCulture = new RequestCulture(defaultCulture);
    });

    // Forwarded headers â€” KnownProxies loaded from config so clients cannot spoof X-Forwarded-For
    services.Configure<ForwardedHeadersOptions>(options =>
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        options.KnownIPNetworks.Clear();
        options.KnownProxies.Clear();

        var proxyIps = configuration.GetSection("ReverseProxy:TrustedProxies").Get<string[]>() ?? [];
        foreach (var ip in proxyIps)
            if (IPAddress.TryParse(ip, out var addr))
                options.KnownProxies.Add(addr);
    });
}

static async Task SeedDatabase(IServiceProvider services, bool isDevelopment, ILogger<AdhdTimeOrganizer.Program> logger)
{
    try
    {
        using var scope = services.CreateScope();
        var scopedServices = scope.ServiceProvider; // Use the scoped provider

        logger.LogInformation("Starting database seeding...");

        // Four passes, in this order â€” later passes need what earlier ones create. Every call is
        // commented out on purpose: seeding is run deliberately, not on every boot. The dev passes
        // truncate, so never uncomment them outside the `isDevelopment` guard.

        // 1. App-wide production data: roles, then the root admin (whose own per-user defaults
        //    DefaultUsersSeeder creates as part of creating the account). Upserts, never truncates,
        //    so this one is safe to run in any environment.
        var appWideDefaults = scopedServices.GetRequiredService<IAppWideDefaultSeederManager>();
        // await appWideDefaults.SeedAllAsync();

        // 2. Replay per-user defaults across existing accounts â€” for when a default's definition
        //    changed after those accounts were created. `overrideData: true` rewrites the users'
        //    existing default rows in place, keeping their ids.
        var perUserDefaults = scopedServices.GetRequiredService<IPerUserDefaultSeederManager>();
        if (isDevelopment)
        {
            // await perUserDefaults.SeedAllForAllUsersAsync(true);
        }

        // 3. Per-user dev fixtures, attached to the root admin. Runs before pass 4 because the module
        //    fixtures there have the highest Order values and used to run last when both kinds shared
        //    a single ordered list.
        var perUserDev = scopedServices.GetRequiredService<IPerUserDevSeederManager>();
        if (isDevelopment)
        {
            // await perUserDev.SeedAllForRootAdminAsync(true);
        }

        // 4. App-wide dev fixtures â€” the module ones (notifications, reminders), which pick their own
        //    owners through ISeedUserProvider rather than being handed a single user.
        var appWideDev = scopedServices.GetRequiredService<IAppWideDevSeederManager>();
        if (isDevelopment)
        {
            // await appWideDev.SeedAllAsync(true);
        }

        logger.LogInformation("Database seeding completed.");
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "An error occurred while seeding the database.");
        throw;
    }
}

static void ConfigurePipeline(WebApplication app, ILogger<AdhdTimeOrganizer.Program> logger)
{
    // Application stopping event
    app.Lifetime.ApplicationStopping.Register(() =>
    {
        logger.LogInformation("Application is stopping...");
        Console.WriteLine("<< STOPPING called. Stack:");
        Console.WriteLine(Environment.StackTrace);
    });

    // Must be first so real client IP is resolved before any logging
    app.UseForwardedHeaders();
    // Stamp the client-IP header from the (now-resolved) RemoteIpAddress so Throttle() keys are
    // non-spoofable. Must stay directly after UseForwardedHeaders â€” see TrustedIpMiddleware.
    app.UseTrustedClientIpHeader();
    app.UseHttpsRedirection();

    // Swallow client-disconnect cancellations â€” not a server error
    app.Use(async (context, next) =>
    {
        try
        {
            await next(context);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            context.Response.StatusCode = 499;
        }
    });

    if (app.Environment.IsDevelopment())
        app.UseSwaggerGen();

    app.UseSerilogRequestLogging(options =>
    {
        options.MessageTemplate = "HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.0000} ms";
        options.EnrichDiagnosticContext = (diagnosticContext, httpContext) =>
        {
            diagnosticContext.Set("RequestHost", httpContext.Request.Host.Value);
            diagnosticContext.Set("RequestScheme", httpContext.Request.Scheme);
            diagnosticContext.Set("UserAgent", httpContext.Request.Headers.UserAgent.ToString());
            diagnosticContext.Set("ClientIP", httpContext.Connection.RemoteIpAddress?.ToString());

            if (httpContext.Request.Method != "GET" && httpContext.Request.ContentLength > 0)
            {
                httpContext.Request.EnableBuffering();
                httpContext.Request.Body.Position = 0;
                using var reader = new StreamReader(httpContext.Request.Body, leaveOpen: true);
                var body = reader.ReadToEndAsync().Result;
                httpContext.Request.Body.Position = 0;

                if (body.Length > 1000)
                    body = body.Substring(0, 1000) + "... (truncated)";
                diagnosticContext.Set("RequestBody", body);
            }
        };
        options.GetLevel = (httpContext, elapsed, ex) =>
        {
            if (ex is OperationCanceledException && httpContext.RequestAborted.IsCancellationRequested)
                return LogEventLevel.Debug;
            if (ex != null || httpContext.Response.StatusCode >= 500)
                return LogEventLevel.Error;
            if (httpContext.Response.StatusCode >= 400)
                return LogEventLevel.Warning;
            return LogEventLevel.Information;
        };
    });

    app.UseCors("AllowFrontend");

    app.UseCookiePolicy();
    app.UseSession();

    app.UseRequestLocalization();
    app.UseAuthentication();
    app.UseAuthorization();

    // FastEndpoints configuration (replaces MapControllers)
    app.UseFastEndpoints(config =>
    {
        config.Endpoints.RoutePrefix = "api";
        config.Endpoints.ShortNames = true;
        config.Endpoints.Configurator = ep =>
        {
            if (ep.AllowedRoles is null || ep.AllowedRoles.Count == 0)
                ep.Roles("User", "Admin", "Root");

            // Extension access is deny-by-default: every endpoint except those explicitly marked
            // [AllowExtensionClients] gets the refusing policy. This must be applied per endpoint
            // rather than via AuthorizationOptions.FallbackPolicy â€” the Roles() call above gives every
            // endpoint authorization metadata, and an endpoint carrying any such metadata never
            // reaches the fallback, which is why the fallback silently protected nothing.
            if (!ep.EndpointType.IsDefined(typeof(AllowExtensionClientsAttribute), true))
                ep.Policies(ExtensionClientPolicies.DenyExtensionClients);
        };
    });

    // Live notification channel for the Notifications module. The hub itself is [Authorize]d and targets
    // individual users via Clients.User(userId) keyed on the NameIdentifier claim.
    app.MapHub<NotificationHub>("/hubs/notifications");
}

static void LoadSettingsFromConfiguration(IConfiguration configuration, IServiceCollection services)
{
    services.Configure<TodoListSettings>(
        configuration.GetSection("TodoListSettings")
    );

    // 2FA policy. Absent config keeps the defaults: opt-in per user, and a Google sign-in counts as
    // the second factor. See Sydowwe.Framework/config/TwoFactorOptions.cs.
    services.Configure<TwoFactorOptions>(
        configuration.GetSection(TwoFactorOptions.SectionName)
    );
}

namespace AdhdTimeOrganizer
{
    public partial class Program
    {
    }
}