using System.Globalization;
using System.Text.Json.Serialization;
using AdhdTimeOrganizer.config;
using AdhdTimeOrganizer.config.dependencyInjection;
using AdhdTimeOrganizer.domain.helper;
using AdhdTimeOrganizer.infrastructure.persistence;
using AdhdTimeOrganizer.infrastructure.persistence.seeder.manager;
using DotNetEnv;
using FastEndpoints;
using FastEndpoints.Swagger;
using Microsoft.AspNetCore.CookiePolicy;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.EntityFrameworkCore;
using Serilog;

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
    SerilogConfig.ConfigureSerilog(builder.Configuration, builder.Host, "a" ?? DatabaseStringsHelper.GetLogDatabaseConnectionString);

    // Configure services
    ConfigureServices(builder.Configuration, builder.Services, builder.Environment.IsDevelopment());

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
    services.AddDbContext<AppCommandDbContext>(options =>
        options.UseNpgsql(DatabaseStringsHelper.GetCommandDatabaseConnectionString, b => b.MigrationsAssembly("MojaDigitalnaFirma.Sandbox.AdminPortal"))
            .UseSnakeCaseNamingConvention()
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
        options.AddPolicy("AllowFrontend", corsBuilder =>
        {
            var origins = new List<string> { "https://localhost:3000", "https://localhost:5173", pageUrl };
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

        var devSeeder = scopedServices.GetService<IDevSeederManager>();
        if (devSeeder != null && isDevelopment)
        {
            // await devSeeder.SeedAllAsync();
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

    // Request pipeline configuration
    app.UseSerilogRequestLogging();
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