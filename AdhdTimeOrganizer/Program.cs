using System.Globalization;
using System.Text.Json.Serialization;
using AdhdTimeOrganizer.config;
using AdhdTimeOrganizer.config.dependencyInjection;
using AdhdTimeOrganizer.domain.helper;
using AdhdTimeOrganizer.infrastructure.jobs;
using AdhdTimeOrganizer.infrastructure.persistence;
using AdhdTimeOrganizer.infrastructure.persistence.seeder.dev;
using AdhdTimeOrganizer.infrastructure.persistence.seeder.@interface.manager;
using AdhdTimeOrganizer.infrastructure.settings;
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
    var logger = app.Services.GetRequiredService<ILogger<Program>>();

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

    // Database configuration
    services.AddDbContext<AppDbContext>(options =>
        options.UseNpgsql(DatabaseStringsHelper.GetDefaultDatabaseConnectionString, b => b.MigrationsAssembly(typeof(Program).Assembly.FullName))
            .UseSnakeCaseNamingConvention()
            .ReplaceService<IMigrationsSqlGenerator, PartitionedNpgsqlMigrationsSqlGenerator>()
            .LogTo(Console.WriteLine));

    // Dependency injection
    try
    {
        services.AddDependencyInjection();
    }
    catch (Exception e)
    {
        Log.Fatal(e, "Failed to configure dependency injection");
        throw;
    }

    // Identity services

    // FastEndpoints
    services.AddFastEndpoints();
    if (isDevelopment)
    {
        services.SwaggerDocument();
    }

    services.AddIdentityServices();

    // Background services
    services.AddHostedService<AdhdTimeOrganizer.infrastructure.extService.RefreshTokenCleanupService>();

    services.AddQuartz(q =>
    {
        q.AddJob<RoutineTodoListResetJob>(opts =>
            opts.WithIdentity("routine-reset", "routine"));

        q.AddTrigger(opts => opts
            .ForJob("routine-reset", "routine")
            .WithIdentity("routine-reset-trigger", "routine")
            .WithCronSchedule("0 0 2 * * ?")); // 2:00 AM daily
    });
    services.AddQuartzHostedService(q => q.WaitForJobsToComplete = true);

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
    services.ConfigureHttpJsonOptions(options => { options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()); });

    services.ConfigureHttpJsonOptions(options => { options.SerializerOptions.Converters.Add(new DateOnlyJsonConverter()); });
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
            {
                origins.Add($"chrome-extension://{extensionId}");
            }

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

    // Forwarded headers
    services.Configure<ForwardedHeadersOptions>(options => { options.ForwardedHeaders = ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedFor; });
}

static async Task SeedDatabase(IServiceProvider services, bool isDevelopment, ILogger<Program> logger)
{
    try
    {
        using var scope = services.CreateScope();
        var scopedServices = scope.ServiceProvider; // Use the scoped provider

        logger.LogInformation("Starting database seeding...");

        var defaultSeeder = scopedServices.GetService<IDefaultSeederManager>();

        if (defaultSeeder != null)
        {
            // await defaultSeeder.SeedAllAsync();
        }

        var userDefaultsSeeder = scopedServices.GetService<IUserDefaultSeederManager>();
        if (userDefaultsSeeder != null && isDevelopment)
        {
            // await userDefaultsSeeder.SeedAllForAllUsersAsync(true);
        }

        var devSeeder = scopedServices.GetService<IDevSeederManager>();
        if (devSeeder != null && isDevelopment)
        {
            // await devSeeder.SeedAllAsync(true);
        }

        logger.LogInformation("Database seeding completed.");
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "An error occurred while seeding the database.");
        throw;
    }
}

static void ConfigurePipeline(WebApplication app, ILogger<Program> logger)
{
    // Application stopping event
    app.Lifetime.ApplicationStopping.Register(() =>
    {
        logger.LogInformation("Application is stopping...");
        Console.WriteLine("<< STOPPING called. Stack:");
        Console.WriteLine(Environment.StackTrace);
    });

    // Culture middleware
    app.Use(async (context, next) =>
    {
        var uiCulture = context.Request.Headers.AcceptLanguage;

        if (uiCulture.Count > 0)
        {
            try
            {
                var culture = new CultureInfo(uiCulture[0]?.Split(",")[0] ?? "sk-SK");
                Thread.CurrentThread.CurrentCulture = culture;
                Thread.CurrentThread.CurrentUICulture = culture;
                CultureInfo.CurrentCulture = culture;
                CultureInfo.CurrentUICulture = culture;
            }
            catch (CultureNotFoundException)
            {
                // Fallback to default culture if requested culture is not supported
                var defaultCulture = new CultureInfo("sk-SK");
                Thread.CurrentThread.CurrentCulture = defaultCulture;
                Thread.CurrentThread.CurrentUICulture = defaultCulture;
            }
        }

        logger.LogDebug("Request: {RequestPath}", context.Request.Path);
        await next();
        logger.LogDebug("Response: {StatusCode}", context.Response.StatusCode);
    });

    // Development-specific middleware
    if (app.Environment.IsDevelopment())
    {
        app.UseSwaggerGen(); // FastEndpoints Swagger
    }

    // Middleware to capture and log response body for errors
    app.Use(async (context, next) =>
    {
        var originalBodyStream = context.Response.Body;
        using var responseBody = new MemoryStream();
        context.Response.Body = responseBody;

        await next();

        responseBody.Seek(0, SeekOrigin.Begin);
        
        // Log response body for 4xx and 5xx errors
        if (context.Response.StatusCode >= 400)
        {
            var responseText = await new StreamReader(responseBody).ReadToEndAsync();
            responseBody.Seek(0, SeekOrigin.Begin);

            var logLevel = context.Response.StatusCode >= 500 ? LogLevel.Error : LogLevel.Warning;
            logger.Log(logLevel, 
                "HTTP {Method} {Path} {StatusCode} - Response: {ResponseBody}", 
                context.Request.Method, 
                context.Request.Path, 
                context.Response.StatusCode,
                responseText.Length > 2000 ? responseText.Substring(0, 2000) + "... (truncated)" : responseText);
            responseBody.Seek(0, SeekOrigin.Begin);
        }

        await responseBody.CopyToAsync(originalBodyStream);
    });

    // Request pipeline configuration
    app.UseSerilogRequestLogging(options =>
    {
        options.MessageTemplate = "HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.0000} ms";
        options.EnrichDiagnosticContext = (diagnosticContext, httpContext) =>
        {
            diagnosticContext.Set("RequestHost", httpContext.Request.Host.Value);
            diagnosticContext.Set("RequestScheme", httpContext.Request.Scheme);
            diagnosticContext.Set("UserAgent", httpContext.Request.Headers.UserAgent.ToString());
            diagnosticContext.Set("ClientIP", httpContext.Connection.RemoteIpAddress?.ToString());
            
            // Log request body for non-GET requests (be careful with sensitive data)
            if (httpContext.Request.Method != "GET" && httpContext.Request.ContentLength > 0)
            {
                httpContext.Request.EnableBuffering();
                httpContext.Request.Body.Position = 0;
                using var reader = new StreamReader(httpContext.Request.Body, leaveOpen: true);
                var body = reader.ReadToEndAsync().Result;
                httpContext.Request.Body.Position = 0;
                
                // Truncate large bodies
                if (body.Length > 1000)
                {
                    body = body.Substring(0, 1000) + "... (truncated)";
                }
                diagnosticContext.Set("RequestBody", body);
            }
        };
        options.GetLevel = (httpContext, elapsed, ex) =>
        {
            if (ex != null || httpContext.Response.StatusCode >= 500)
                return LogEventLevel.Error;
            if (httpContext.Response.StatusCode >= 400)
                return LogEventLevel.Warning;
            return LogEventLevel.Information;
        };
    });
    app.UseForwardedHeaders();
    app.UseHttpsRedirection();

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
    });
}

static void LoadSettingsFromConfiguration(IConfiguration configuration, IServiceCollection services)
{
    services.Configure<TodoListSettings>(
        configuration.GetSection("TodoListSettings")
    );
}

public partial class Program { }