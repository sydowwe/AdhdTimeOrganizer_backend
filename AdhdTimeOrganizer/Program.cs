using System.Globalization;
using System.Net;
using System.Text.Json.Serialization;
using AdhdTimeOrganizer.ActivityProfiles.domain.model.entity;
using AdhdTimeOrganizer.ActivityProfiles.domain.service;
using AdhdTimeOrganizer.ActivityProfiles.infrastructure.extService.weather;
using AdhdTimeOrganizer.application.endpoint.@base;
using AdhdTimeOrganizer.config;
using AdhdTimeOrganizer.config.dependencyInjection;
using AdhdTimeOrganizer.config.swagger;
using AdhdTimeOrganizer.Core.domain.model.entity.activity;
using AdhdTimeOrganizer.Core.infrastructure.security;
using AdhdTimeOrganizer.History.domain.model.entity.activityHistory;
using AdhdTimeOrganizer.infrastructure.persistence;
using AdhdTimeOrganizer.infrastructure.persistence.interceptors;
using AdhdTimeOrganizer.infrastructure.scheduling;
using AdhdTimeOrganizer.Planning.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.Routines.domain.model.entity.todoList;
using AdhdTimeOrganizer.Routines.infrastructure.scheduling;
using AdhdTimeOrganizer.TodoLists.domain.model.entity.todoList;
using AdhdTimeOrganizer.TodoLists.infrastructure.persistence.interceptor;
using AdhdTimeOrganizer.TodoLists.infrastructure.settings;
using AdhdTimeOrganizer.Tracking.domain.model.entity.activityTracking.desktop;
using AdhdTimeOrganizer.Tracking.infrastructure.persistence.retention;
using AdhdTimeOrganizer.Tracking.infrastructure.scheduling;
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
using Serilog;
using Serilog.Events;
using Sydowwe.Framework.application.middleware;
using Sydowwe.Framework.config;
using Sydowwe.Framework.domain.auth;
using Sydowwe.Framework.domain.helper;
using Sydowwe.Framework.infrastructure.extService.user.auth;
using Sydowwe.Framework.infrastructure.persistence;
using Sydowwe.Framework.infrastructure.persistence.seeder.@interface.manager;
using Sydowwe.Framework.infrastructure.security;
using Sydowwe.Notifications.domain.entity;
using Sydowwe.Notifications.infrastructure.realtime;
using Sydowwe.Notifications.infrastructure.scheduling;
using Sydowwe.Reminders.domain.entity;
using Sydowwe.Reminders.infrastructure.scheduling;
using Sydowwe.Scheduler;
using Sydowwe.Scheduler.domain.entity;
using Sydowwe.Scheduler.infrastructure.scheduling;

try
{
    // Load environment variables
    Env.Load();

    EnsureFieldEncryptionKey();

    // Set default culture
    var defaultCulture = new CultureInfo("sk-SK");
    CultureInfo.DefaultThreadCurrentCulture = defaultCulture;
    CultureInfo.DefaultThreadCurrentUICulture = defaultCulture;

    var builder = WebApplication.CreateBuilder(args);

    // Configure configuration sources. Re-added over CreateBuilder's own so the base path is the deployed
    // output directory rather than the working directory.
    builder.Configuration
        .SetBasePath(AppContext.BaseDirectory)
        .AddJsonFile("appsettings.json", false, true)
        .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", true, true)
        // Last source wins, so the two JSON files above would otherwise override the environment — the
        // reverse of the standard precedence, and it silently breaks every documented `Section__Key`
        // override (docs/notificationSetup.md configures the VAPID private key exactly that way, and
        // appsettings placeholders would shadow it). Re-adding the provider here restores env-over-JSON.
        .AddEnvironmentVariables();

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

    // The suggestion-pattern materialized views are hand-written SQL, so no migration creates them.
    // They must exist before anything saves a Calendar / PlannerTask / ActivityHistory, because
    // SuggestionPatternRefreshInterceptor REFRESHes them on save - seeding included.
    await EnsureSuggestionPatternViews(app.Services, logger);

    // Database seeding. Off unless Seeding:RunOnStartup says otherwise — switch it on (appsettings,
    // appsettings.Development.json, or Seeding__RunOnStartup=true) for the boot after a migration, then
    // switch it back. IncludeDevFixtures adds passes 2-4 on top (two of them truncate); see SeedDatabase.
    var seeding = builder.Configuration.GetSection("Seeding");
    if (seeding.GetValue<bool>("RunOnStartup"))
        await SeedDatabase(app.Services, seeding.GetValue<bool>("IncludeDevFixtures"), logger);


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

/// <summary>
/// Fails the boot immediately, and legibly, when <c>FIELD_ENCRYPTION_KEY</c> is missing or malformed.
/// <para>
/// <b>Why this exists.</b> <c>User.GoogleCalendarRefreshToken</c> and
/// <c>DesktopActivityEntry.ExecutablePath</c> use <c>EncryptedColumn</c>, which resolves
/// <c>AesGcmFieldEncryptor.Shared</c> during <c>OnModelCreating</c>. Without this guard a missing key
/// surfaces as an <c>EnvironmentVariableMissingException</c> wrapped in a model-building failure at the
/// first DbContext use — long after startup "succeeded", and reading as an EF problem rather than a
/// deployment one. Checking here turns that into one clear line at boot.
/// </para>
/// <para>
/// <b>Deployment note.</b> The Docker image copies no <c>.env</c>, so in a container this must be
/// supplied by the runtime (<c>-e</c>, compose <c>environment:</c>, or an orchestrator secret). It is
/// deliberately not baked into the Dockerfile — that would commit a live encryption key to the repo.
/// Generate one with:
/// <c>[Convert]::ToBase64String([System.Security.Cryptography.RandomNumberGenerator]::GetBytes(32))</c>
/// </para>
/// <para>
/// ⚠ Rotating the key makes every existing <c>enc:v1:</c> token undecryptable. The <c>v1</c> prefix
/// leaves room for a staged rotation, but no rotation tooling exists yet.
/// </para>
/// </summary>
static void EnsureFieldEncryptionKey()
{
    const string keyName = "FIELD_ENCRYPTION_KEY";
    const string howTo =
        "Set it to a base64-encoded 32-byte value. In development it belongs in AdhdTimeOrganizer/.env; " +
        "in a container it must come from the runtime environment, since no .env is copied into the image.";

    var value = Environment.GetEnvironmentVariable(keyName);
    if (string.IsNullOrWhiteSpace(value))
        throw new InvalidOperationException(
            $"{keyName} is not set, and encrypted entity columns cannot be configured without it. {howTo}");

    byte[] key;
    try
    {
        key = Convert.FromBase64String(value);
    }
    catch (FormatException)
    {
        throw new InvalidOperationException($"{keyName} is not valid base64. {howTo}");
    }

    if (key.Length != 32)
        throw new InvalidOperationException(
            $"{keyName} must decode to 32 bytes for AES-256; got {key.Length}. {howTo}");
}

static void ConfigureServices(IConfiguration configuration, IServiceCollection services, bool isDevelopment)
{
    // HTTP context accessor
    services.AddHttpContextAccessor();

    // Interceptors
    services.AddSingleton<ISuggestionPatternRefreshQueue, SuggestionPatternRefreshQueue>();
    services.AddScoped<SuggestionPatternRefreshInterceptor>();

    // Stamps TodoListItem.CompletedTimestamp on every IsDone transition, wherever it is written from.
    // Stateless, hence singleton. Dropping this registration breaks nothing loudly — items still
    // complete, they just stop appearing in the daily recap.
    services.AddSingleton<TodoListItemCompletionInterceptor>();

    // Per-user global query filters (BaseDbContext). Framework defaults this off; in this portal every
    // row belongs to exactly one user and nothing reads across users, so it is on by default here.
    // Bound after the code default so a deployment can still switch it off via UserScoping:Enabled.
    services.Configure<UserScopingOptions>(options => options.Enabled = true);
    services.Configure<UserScopingOptions>(configuration.GetSection(UserScopingOptions.SectionName));

    // Database configuration
    services.AddDbContext<AppDbContext>((sp, options) =>
    {
        options.UseNpgsql(DatabaseStringsHelper.GetDefaultDatabaseConnectionString, b => b.MigrationsAssembly(typeof(AdhdTimeOrganizer.Program).Assembly.FullName))
            .UseSnakeCaseNamingConvention()
            .ReplaceService<IMigrationsSqlGenerator, PartitionedNpgsqlMigrationsSqlGenerator>()
            .AddInterceptors(
                sp.GetRequiredService<SuggestionPatternRefreshInterceptor>(),
                sp.GetRequiredService<TodoListItemCompletionInterceptor>());
        if (isDevelopment)
            options.LogTo(Console.WriteLine);
    });

    // Dependency injection
    services.AddDependencyInjection();
    services.AddModuleServices(configuration);

    // Identity services

    // Role catalog. Must precede AddFastEndpoints() -- endpoint Configure() resolves its role gate
    // through FrameworkRoles during registration, and an unconfigured catalog throws there.
    FrameworkRoles.Configure(PortalRoleCatalog.Create(), RoleTier.User);

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
            // AdhdTimeOrganizer.Core carries the 78 activity endpoints and the 10 timer ones. A slice
            // missing from this list is not a build error — its routes simply never register and 404.
            typeof(Activity).Assembly,
            // AdhdTimeOrganizer.TodoLists — lists, items, steps, categories and priorities.
            typeof(TodoList).Assembly,
            // AdhdTimeOrganizer.History — the history CRUD/grid plus the six dashboard endpoints.
            // CalendarActivityEndpoint is deliberately NOT here: it reads the Calendar entity, so it
            // stayed host-side and is covered by typeof(Program).Assembly above.
            typeof(ActivityHistory).Assembly,
            // AdhdTimeOrganizer.Routines — the routine to-do list and time period endpoints.
            typeof(RoutineTimePeriod).Assembly,
            // AdhdTimeOrganizer.Planning — the 44 planner/calendar/template endpoints plus the five
            // reminder ones (reminders are part of this slice). SyncCalendarToGoogleEndpoint is
            // deliberately NOT here: the Google integration stayed host-side and is covered by
            // typeof(Program).Assembly above.
            typeof(PlannerTask).Assembly,
            // AdhdTimeOrganizer.Tracking — the 29 ingest / pattern-mapping / dashboard endpoints for
            // desktop, web-extension and android. Note the ingest ones carry [AllowExtensionClients]
            // and the ActivityTracking policy: if this entry is missing they 404, but if the attribute
            // were lost in the move they would 403 instead — two different silent failures, so
            // TrackingRouteSmokeTests checks both.
            typeof(DesktopActivityEntry).Assembly,
            // AdhdTimeOrganizer.ActivityProfiles — the 52 backlog / bucket-list / project profile,
            // memory-anchor and activity-lookup endpoints. This is the largest single block of routes
            // outside the host, so a missing entry here is a very visible 404 storm rather than a
            // subtle one; ActivityProfilesRouteSmokeTests covers one route per endpoint family.
            typeof(ActivityBacklogProfile).Assembly,
            typeof(Notification).Assembly,
            typeof(ReminderDefinition).Assembly,
            typeof(ScheduledJob).Assembly
        ];
    });
    services.AddSingleton<IGlobalPostProcessor, ErrorLoggingPostProcessor>();
    if (isDevelopment)
        services.SwaggerDocument(o =>
        {
            o.DocumentSettings = s =>
            {
                // ICreateRequest<TEntity>.ToEntity pulls the raw EF navigation graph into the schema — several
                // of those graphs are cyclic and overflow the stack inside FastEndpoints' validation schema
                // processor. See RemoveToEntitySchemaProcessor for the full explanation.
                //
                // PrependTo, NOT Add: order is load-bearing. FastEndpoints registers its own
                // ValidationSchemaProcessor inside EnableFastEndpoints and only *then* invokes this
                // DocumentSettings action, so a plain Add() puts us behind it — ToEntity would still be on
                // the schema when that processor walks it, and walking the cyclic EF navigation graph is what
                // killed the process with a StackOverflowException on the first /swagger/v1/swagger.json
                // request. Pinned by SwaggerSchemaProcessorOrderTests.
                RemoveToEntitySchemaProcessor.PrependTo(s.SchemaSettings.SchemaProcessors);
            };
        });

    services.AddIdentityServices();

    services.Configure<ActivityTrackingRetentionOptions>(
        configuration.GetSection(ActivityTrackingRetentionOptions.SectionName));

    // The leisure weather signal (GET /leisure-weather-fit). Registered here by name rather than picked up by a
    // marker scan on purpose: ActivityProfiles is in ModuleAssemblies, so a lifetime marker on the provider would
    // register it a second time, and a typed HttpClient cannot be produced by those scans at all.
    //
    // The timeout is the load-bearing part. The picker calls this alongside the draw and never retries, so a
    // provider that hangs must become "no weather opinion" in seconds rather than hold a request thread — every
    // failure inside OpenMeteoWeatherProvider, this timeout included, comes back as a null and then as an empty
    // matching set.
    services.Configure<LeisureWeatherOptions>(configuration.GetSection(LeisureWeatherOptions.SectionName));
    services.AddMemoryCache();
    services.AddHttpClient<IDailyWeatherProvider, OpenMeteoWeatherProvider>(client =>
        client.Timeout = TimeSpan.FromSeconds(5));

    // Background services
    services.AddHostedService<RefreshTokenCleanupService>();

    // The whole scheduling substrate, owned by Sydowwe.Scheduler: the single AddQuartz call, the generic
    // dispatcher job every IScheduler-registered trigger points at, and the Quartz hosted service. Nothing
    // else here (host or slice) references Quartz — a job is a keyed IScheduledJobHandler plus a
    // RecurringJobRegistration pushed by its owner's registrar below. Do NOT add a second AddQuartz call or a
    // host-side AddJob<T>: that reintroduces the coupling this replaced, and a stray AddQuartz silently
    // reconfigures the same scheduler.
    services.AddSchedulerSubstrate();

    // Owner-side boot reconciliation: each module and slice pushes its recurring-job registrations to the
    // Scheduler via Sydowwe.Framework.Contracts's IScheduler. Idempotent upserts by JobKey, and required on
    // every boot because the Quartz RAM job store loses all triggers on restart.
    services.AddHostedService<NotificationsScheduledJobsRegistrar>();
    services.AddHostedService<RemindersScheduledJobsRegistrar>();
    services.AddHostedService<SchedulerScheduledJobsRegistrar>();
    services.AddHostedService<RoutinesScheduledJobsRegistrar>();
    services.AddHostedService<TrackingScheduledJobsRegistrar>();
    services.AddHostedService<PortalScheduledJobsRegistrar>();

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
            var origins = new List<string> { "https://localhost:3000", "https://localhost:5173" };
            if (!string.IsNullOrEmpty(pageUrl))
                origins.Add(pageUrl);

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

    // Forwarded headers — KnownProxies loaded from config so clients cannot spoof X-Forwarded-For
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

static async Task EnsureSuggestionPatternViews(IServiceProvider services, ILogger<AdhdTimeOrganizer.Program> logger)
{
    using var scope = services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await SuggestionPatternViewInstaller.EnsureViewsCreatedAsync(db, logger);
}

/// <summary>
/// Runs the four seeding passes. Only reached when <c>Seeding:RunOnStartup</c> is set — every host boot,
/// tests included, would otherwise reseed (and, for the dev passes, truncate) behind your back.
/// <paramref name="includeDevFixtures"/> comes from <c>Seeding:IncludeDevFixtures</c> and gates the three
/// passes that are fixtures rather than production data.
/// </summary>
static async Task SeedDatabase(IServiceProvider services, bool includeDevFixtures, ILogger<AdhdTimeOrganizer.Program> logger)
{
    try
    {
        using var scope = services.CreateScope();
        var scopedServices = scope.ServiceProvider; // Use the scoped provider

        logger.LogInformation("Starting database seeding...");

        // Four passes, in this order — later passes need what earlier ones create. Seeding is run
        // deliberately, not on every boot, which is what Seeding:RunOnStartup at the call site is for.
        // The dev passes truncate, so never take them outside the `includeDevFixtures` guard.

        // 1. App-wide production data: roles, then the root admin (whose own per-user defaults
        //    DefaultUsersSeeder creates as part of creating the account). Upserts, never truncates,
        //    so this one is safe to run in any environment.
        var appWideDefaults = scopedServices.GetRequiredService<IAppWideDefaultSeederManager>();
        await appWideDefaults.SeedAllAsync();

        // 2. Replay per-user defaults across existing accounts — for when a default's definition
        //    changed after those accounts were created. `overrideData: true` rewrites the users'
        //    existing default rows in place, keeping their ids.
        var perUserDefaults = scopedServices.GetRequiredService<IPerUserDefaultSeederManager>();
        if (includeDevFixtures)
        {
            await perUserDefaults.SeedAllForAllUsersAsync(true);
        }

        // 3. Per-user dev fixtures, attached to the root admin. Runs before pass 4 because the module
        //    fixtures there have the highest Order values and used to run last when both kinds shared
        //    a single ordered list.
        var perUserDev = scopedServices.GetRequiredService<IPerUserDevSeederManager>();
        if (includeDevFixtures)
        {
            await perUserDev.SeedAllForRootAdminAsync(true);
        }

        // 4. App-wide dev fixtures — the module ones (notifications, reminders), which pick their own
        //    owners through ISeedUserProvider rather than being handed a single user.
        var appWideDev = scopedServices.GetRequiredService<IAppWideDevSeederManager>();
        if (includeDevFixtures)
        {
            await appWideDev.SeedAllAsync(true);
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
    });

    // Must be first so real client IP is resolved before any logging
    app.UseForwardedHeaders();
    // Stamp the client-IP header from the (now-resolved) RemoteIpAddress so Throttle() keys are
    // non-spoofable. Must stay directly after UseForwardedHeaders — see TrustedIpMiddleware.
    app.UseTrustedClientIpHeader();
    app.UseHttpsRedirection();

    // Swallow client-disconnect cancellations — not a server error
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

            // Deliberately no request-body capture: login/register/change-password bodies carry
            // plaintext passwords, and every other write body carries emails/names — the Postgres sink's
            // PropertiesColumnWriter persists every diagnostic-context property into a queryable column
            // with no redaction (PiiRedactor isn't wired into this pipeline).
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
            // rather than via AuthorizationOptions.FallbackPolicy — the Roles() call above gives every
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
    // the second factor. See framework/Sydowwe.Framework/config/TwoFactorOptions.cs.
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