using System.Net;
using System.Net.Http.Json;
using AdhdTimeOrganizer.application.dto.response;
using AdhdTimeOrganizer.domain.model.entity.activity;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using FluentAssertions;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Endpoints;

public class BaseGetAllEndpointTests(TestWebApplicationFactory factory) : IntegrationTestBase(factory)
{
    private const string Route = "/api/activity-category";

    private async Task SeedCategoriesAsync()
    {
        var db = CreateDbContext();
        var userId = await GetTestUserIdAsync();
        db.ActivityCategories.AddRange(
            new ActivityCategory { Name = "Cat A", Color = "#FF0000", UserId = userId },
            new ActivityCategory { Name = "Cat B", Color = "#00FF00", UserId = userId });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task GetAll_WithData_Returns200WithList()
    {
        await SeedCategoriesAsync();

        var response = await Client.GetAsync(Route);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var items = await response.Content.ReadFromJsonAsync<List<ActivityCategoryResponse>>();
        items.Should().NotBeNull();
        items!.Count.Should().BeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public async Task GetAll_WithoutAuth_Returns401()
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
