using AdhdTimeOrganizer.Command.application.@interface.activity;
using AdhdTimeOrganizer.Command.application.@interface.activityHistory;
using AdhdTimeOrganizer.Command.application.@interface.activityPlanning;
using AdhdTimeOrganizer.Command.application.@interface.users;
using AdhdTimeOrganizer.Command.application.mapper;
using AdhdTimeOrganizer.Command.application.service.activity;
using AdhdTimeOrganizer.Command.application.service.activityHistory;
using AdhdTimeOrganizer.Command.application.service.activityPlanning;
using AdhdTimeOrganizer.Command.application.service.user;
using AdhdTimeOrganizer.Command.domain.@event;
using AdhdTimeOrganizer.Command.domain.repositoryContract.activity;
using AdhdTimeOrganizer.Command.domain.repositoryContract.activityHistory;
using AdhdTimeOrganizer.Command.domain.repositoryContract.activityPlanning;
using AdhdTimeOrganizer.Command.domain.serviceContract;
using AdhdTimeOrganizer.Command.infrastructure.extService;
using AdhdTimeOrganizer.Command.infrastructure.repository.activity;
using AdhdTimeOrganizer.Command.infrastructure.repository.activityHistory;
using AdhdTimeOrganizer.Command.infrastructure.repository.activityPlanning;

namespace AdhdTimeOrganizer.Web.config;

public static class DependencyInjectionExtensions
{
    public static IServiceCollection AddDependencyInjection(this IServiceCollection services)
    {
        //Repository
        services.AddScoped<IActivityHistoryRepository, ActivityHistoryRepository>();
        services.AddScoped<IActivityRepository, ActivityRepository>();
        services.AddScoped<IAlarmRepository, AlarmRepository>();
        services.AddScoped<ICategoryRepository, CategoryRepository>();
        services.AddScoped<IRoleRepository, RoleRepository>();
        services.AddScoped<IPlannerTaskRepository, PlannerTaskRepository>();
        services.AddScoped<IPlannerTaskRepository, PlannerTaskRepository>();
        services.AddScoped<IRoutineToDoListRepository, RoutineToDoListRepository>();
        services.AddScoped<IRoutineTimePeriodRepository, RoutineTimePeriodRepository>();
        services.AddScoped<IToDoListRepository, ToDoListRepository>();
        services.AddScoped<ITaskUrgencyRepository, TaskUrgencyRepository>();
        services.AddScoped<IWebExtensionDataRepository, WebExtensionDataRepository>();

        //Repository
        services.AddScoped<IActivityHistoryService, ActivityHistoryService>();
        services.AddScoped<IActivityService, ActivityService>();
        services.AddScoped<IAlarmService, AlarmService>();
        services.AddScoped<ICategoryService, CategoryService>();
        services.AddScoped<IRoleService, RoleService>();
        services.AddScoped<IPlannerTaskService, PlannerTaskService>();
        services.AddScoped<IPlannerTaskService, PlannerTaskService>();
        services.AddScoped<IRoutineToDoListService, RoutineToDoListService>();
        services.AddScoped<IRoutineTimePeriodService, RoutineTimePeriodService>();
        services.AddScoped<IToDoListService, ToDoListService>();
        services.AddScoped<ITaskUrgencyService, TaskUrgencyService>();
        services.AddScoped<IWebExtensionDataService, WebExtensionDataService>();
        //User Service
        services.AddScoped<IUserService, UserService>();
        services.AddScoped<ILoggedUserService, LoggedUserService>();
        services.AddScoped<IUserSessionService, UserSessionService>();


        //Service Other
        services.AddHttpClient<IGoogleRecaptchaService, GoogleRecaptchaService>();

        // Configure mail settings
        // services.Configure<MailSettings>(builder.Configuration.GetSection("MailSettings"));
        services.AddTransient<IUserEmailSenderService, UserEmailSenderService>();

        //MAPPER profiles
        services.AddAutoMapper(typeof(UserProfile).Assembly);
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(UserRegisteredEvent).Assembly));

        return services;
    }
}