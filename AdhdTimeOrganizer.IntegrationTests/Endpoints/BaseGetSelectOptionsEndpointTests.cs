using System.Net;
using System.Net.Http.Json;
using AdhdTimeOrganizer.application.dto.response.generic;
using AdhdTimeOrganizer.domain.model.entity.activity;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using FluentAssertions;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Endpoints;

public class BaseGetSelectOptionsEndpointTests(TestWebApplicationFactory factory) : IntegrationTestBase(factory)
{
    private const string Route = "/api/activity-category/all-options";

    private async Task SeedCategoriesAsync()
    {
        var db = CreateDbContext();
        var userId = await GetTestUserIdAsync();
        db.ActivityCategories.AddRange(
            new ActivityCategory { Name = "Option A", Color = "#FF0000", UserId = userId },
            new ActivityCategory { Name = "Option B", Color = "#00FF00", UserId = userId });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task GetSelectOptions_WithData_Returns200WithOptions()
    {
        await SeedCategoriesAsync();

        var response = await Client.GetAsync(Route);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var options = await response.Content.ReadFromJsonAsync<List<SelectOptionResponse>>();
        options.Should().NotBeNull();
        options!.Count.Should().BeGreaterThanOrEqualTo(2);
        options.Should().AllSatisfy(o =>
        {
            o.Id.Should().BeGreaterThan(0);
            o.Text.Should().NotBeNullOrEmpty();
        });
    }

    [Fact]
    public async Task GetSelectOptions_WithoutAuth_Returns401()
    {
        using var anonClient = Factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            HandleCookies = false,
            AllowAutoRedirect = false
        });

        var response = await anonClient.GetAsync(Route);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    public override async Task DisposeAsync()
    {
        var db = CreateDbContext();
        var userId = await GetTestUserIdAsync();
        db.ActivityCategories.RemoveRange(
            db.ActivityCategories.Where(c => c.UserId == userId));
        await db.SaveChangesAsync();
    }
}
