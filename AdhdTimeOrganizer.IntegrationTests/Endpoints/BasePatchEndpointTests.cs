using AdhdTimeOrganizer.Core.domain.model.entity.activity;
using AdhdTimeOrganizer.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.Core.domain.model.@enum;
using AdhdTimeOrganizer.infrastructure.persistence;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.Testing;
using Sydowwe.Framework.Testing.baseTests;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Endpoints;

// Tests BasePatchEndpoint via TemplatePlannerTaskChangeSpanEndpoint ("/template-planner-task/{id}")
[Collection("Postgres")]
public class TemplatePlannerTaskChangeSpanEndpointTests(AppDbContextFixture fixture)
    : BasePatchEndpointTests(fixture)
{
    protected override string EndpointUrl => "api/template-planner-task";

    protected override async Task<long> SeedEntityAsync(DbContext db)
    {
        var ctx = (AppDbContext)db;
        var userId = FakeLoggedUserService.TestUserId;

        var role = new ActivityRole { Name = "Role", Color = "#FF0000", UserId = userId };
        ctx.ActivityRoles.Add(role);
        await ctx.SaveChangesAsync();

        var activity = new Activity { Name = "Activity", RoleId = role.Id, UserId = userId };
        ctx.Activities.Add(activity);
        await ctx.SaveChangesAsync();

        var importance = new TaskImportance { Text = "Required", Importance = 5, UserId = userId };
        ctx.TaskImportances.Add(importance);
        await ctx.SaveChangesAsync();

        var template = new TaskPlannerDayTemplate
        {
            Name = "Template",
            IsActive = true,
            SuggestedForDayType = DayType.Workday,
            UserId = userId
        };
        ctx.TaskPlannerDayTemplates.Add(template);
        await ctx.SaveChangesAsync();

        var task = new TemplatePlannerTask
        {
            ActivityId = activity.Id,
            ImportanceId = importance.Id,
            TemplateId = template.Id,
            StartTime = new TimeOnly(8, 0),
            EndTime = new TimeOnly(9, 0),
            IsBackground = false,
            UserId = userId
        };
        ctx.TemplatePlannerTasks.Add(task);
        await ctx.SaveChangesAsync();

        return task.Id;
    }

    protected override Task<object> BuildValidPayloadAsync(DbContext db, long id) =>
        Task.FromResult<object>(new
        {
            StartTime = new { Hours = 10, Minutes = 0 },
            EndTime = new { Hours = 11, Minutes = 0 }
        });
}