using System.Net;
using System.Net.Http.Json;
using AdhdTimeOrganizer.application.dto.dto;
using AdhdTimeOrganizer.domain.model.entity.activity;
using AdhdTimeOrganizer.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.domain.model.@enum;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Endpoints;

// Tests BasePatchEndpoint via TemplatePlannerTaskChangeSpanEndpoint
public class BasePatchEndpointTests(TestWebApplicationFactory factory) : IntegrationTestBase(factory)
{
    private const string Route = "/api/template-planner-task";

    private async Task<long> SeedTemplatePlannerTaskAsync()
    {
        var db = CreateDbContext();
        var userId = await GetTestUserIdAsync();

        var role = new ActivityRole { Name = "Role", Color = "#FF0000", UserId = userId };
        db.ActivityRoles.Add(role);
        await db.SaveChangesAsync();

        var activity = new Activity { Name = "Activity", RoleId = role.Id, UserId = userId };
        db.Activities.Add(activity);
        await db.SaveChangesAsync();

        var importance = new TaskImportance { Text = "Normal", Importance = 5, UserId = userId };
        db.TaskImportances.Add(importance);
        await db.SaveChangesAsync();

        var template = new TaskPlannerDayTemplate
        {
            Name = "Template",
            IsActive = true,
            SuggestedForDayType = DayType.Workday,
            UserId = userId
        };
        db.TaskPlannerDayTemplates.Add(template);
        await db.SaveChangesAsync();

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
        db.TemplatePlannerTasks.Add(task);
        await db.SaveChangesAsync();

        return task.Id;
    }

    [Fact]
    public async Task Patch_ValidRequest_Returns200WithId()
    {
        var id = await SeedTemplatePlannerTaskAsync();
        var request = new
        {
            StartTime = new { Hours = 10, Minutes = 0 },
            EndTime = new { Hours = 11, Minutes = 0 }
        };

        var response = await Client.PatchAsJsonAsync($"{Route}/{id}", request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var returnedId = await response.Content.ReadFromJsonAsync<long>();
        returnedId.Should().Be(id);
    }

    [Fact]
    public async Task Patch_WithoutAuth_Returns401()
    {
        using var anonClient = Factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            HandleCookies = false,
            AllowAutoRedirect = false
        });
        var request = new
        {
            StartTime = new { Hours = 10, Minutes = 0 },
            EndTime = new { Hours = 11, Minutes = 0 }
        };

        var response = await anonClient.PatchAsJsonAsync($"{Route}/1", request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Patch_NonExistingEntity_Returns404()
    {
        var request = new
        {
            StartTime = new { Hours = 10, Minutes = 0 },
            EndTime = new { Hours = 11, Minutes = 0 }
        };

        var response = await Client.PatchAsJsonAsync($"{Route}/999999999", request);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    public override async Task DisposeAsync()
    {
        var db = CreateDbContext();
        var userId = await GetTestUserIdAsync();
        db.TemplatePlannerTasks.RemoveRange(
            db.TemplatePlannerTasks.Where(t => t.UserId == userId));
        db.TaskPlannerDayTemplates.RemoveRange(
            db.TaskPlannerDayTemplates.Where(t => t.UserId == userId));
        db.TaskImportances.RemoveRange(
            db.TaskImportances.Where(t => t.UserId == userId));
        db.Activities.RemoveRange(
            db.Activities.Where(a => a.UserId == userId));
        db.ActivityRoles.RemoveRange(
            db.ActivityRoles.Where(r => r.UserId == userId));
        await db.SaveChangesAsync();
    }
}
