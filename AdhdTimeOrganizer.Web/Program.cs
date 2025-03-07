using System.Text.Json.Serialization;
using AdhdTimeOrganizer.Command.infrastructure.persistence;
using AdhdTimeOrganizer.Common.domain.helper;
using AdhdTimeOrganizer.Common.infrastructure.persistence;
using AdhdTimeOrganizer.Web.config;
using DotNetEnv;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Microsoft.EntityFrameworkCore;
using NpgsqlTypes;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Serilog.Sinks.PostgreSQL;
using Serilog.Sinks.PostgreSQL.ColumnWriters;

namespace AdhdTimeOrganizer.Web;

internal class Program
{
    public static async Task Main(string[] args)
    {
        try
        {
            Env.Load();

            var builder = WebApplication.CreateBuilder(args);

            builder.Configuration
                .SetBasePath(AppContext.BaseDirectory)
                .AddJsonFile("appsettings.json", false, true)
                .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", true, true);



            builder.Logging.ClearProviders();
            SerilogConfig.ConfigureSerilog(builder.Configuration, builder.Host);

            ConfigureServices(builder.Configuration, builder.Services);

            var app = builder.Build();
            var logger = app.Services.GetRequiredService<ILogger<Program>>();

            logger.LogInformation("Backend starting.");

            using (var scope = app.Services.CreateScope())
            {
                // var commandDbContext = scope.ServiceProvider.GetRequiredService<AppCommandDbContext>();
                // var queryDbContext = scope.ServiceProvider.GetRequiredService<AppQueryDbContext>();
                // if (!await MainHelper.EnsureDatabaseCreatedAsync(commandDbContext)) throw new Exception("Command database could not be reached.");
                // if (!await MainHelper.EnsureDatabaseCreatedAsync(queryDbContext)) throw new Exception("Query database could not be reached.");
            }

            if (!app.Environment.IsDevelopment())
            {
                app.UseSwagger();
                app.UseSwaggerUI();
            }
            else
            {
                app.UseExceptionHandler("/Home/Error");
                app.UseHsts();
            }

            app.UsePathBase(new PathString("/api"));
            app.UseRouting();
            app.UseStaticFiles();
            app.UseCors("AllowFrontend");
            app.UseAuthentication();
            app.UseAuthorization();
            app.UseSession();

            app.UseSerilogRequestLogging();
            app.MapControllers();

            logger.LogInformation("Backend started.");
            await app.RunAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine("Host terminated unexpectedly: \n " + ex.Message);
        }
        finally
        {
            await Log.CloseAndFlushAsync();
        }
    }

    private static void ConfigureServices(IConfiguration configuration, IServiceCollection services)
    {
        services.AddDbContext<AppCommandDbContext>(options =>
            options.UseNpgsql(MainHelper.GetCommandDatabaseConnectionString)
                .LogTo(Console.WriteLine)
        );
        // services.AddDbContext<AppQueryDbContext>(options =>
        //     options.UseNpgsql(MainHelper.GetQueryDatabaseConnectionString)
        //         .LogTo(Console.WriteLine)
        // );

        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen();

        try
        {
            services.AddDependencyInjection();
        }
        catch (Exception e)
        {
            Log.Fatal(e, "Host terminated unexpectedly");
        }

        services.AddIdentityServices();

        services.AddControllers(options => { options.Conventions.Add(new RouteTokenTransformerConvention(new SlugifyParameterTransformer())); })
            .AddJsonOptions(options => { options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()); });

        services.Configure<FormOptions>(options => { options.MultipartBodyLengthLimit = configuration.GetValue<int>("FileUpload:MaxFileSizeInMB") * 1024 * 1024; });
        services.AddCors(options =>
        {
            options.AddPolicy("AllowFrontend", corsBuilder =>
            {
                corsBuilder.WithOrigins("https://localhost:3000", "https://localhost:5104", Helper.GetEnvVar("PAGE_URL"))
                    .AllowAnyHeader()
                    .AllowAnyMethod()
                    .AllowCredentials();
            });
        });

        services.AddHttpContextAccessor();
        services.AddDistributedMemoryCache();
        //appsettings maybe ?
        services.AddSession(options =>
        {
            options.IdleTimeout = TimeSpan.FromMinutes(30);
            options.Cookie.HttpOnly = true;
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            options.Cookie.SameSite = SameSiteMode.Strict;
            options.Cookie.MaxAge = TimeSpan.FromHours(2);
        });
    }
}