using System.Net;
using System.Net.Http.Json;
using AdhdTimeOrganizer.application.dto.response;
using AdhdTimeOrganizer.domain.model.entity.activity;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using FluentAssertions;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Endpoints;

public class BaseGetByIdEndpointTests(TestWebApplicationFactory factory) : IntegrationTestBase(factory)
{
    private const string Route = "/api/activity-category";

    private async Task<long> SeedCategoryAsync()
    {
        var db = CreateDbContext();
        var userId = await GetTestUserIdAsync();
        var category = new ActivityCategory { Name = "GetById Test", Color = "#FF0000", UserId = userId };
        db.ActivityCategories.Add(category);
        await db.SaveChangesAsync();
        return category.Id;
    }

    [Fact]
    public async Task GetById_ExistingEntity_Returns200WithData()
    {
        var id = await SeedCategoryAsync();

        var response = await Client.GetAsync($"{Route}/{id}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var item = await response.Content.ReadFromJsonAsync<ActivityCategoryResponse>();
        item.Should().NotBeNull();
        item!.Id.Should().Be(id);
    }

    [Fact]
    public async Task GetById_WithoutAuth_Returns401()
    {
        using var anonClient = Factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            HandleCookies = false,
            AllowAutoRedirect = false
        });

        var response = await anonClient.GetAsync($"{Route}/1");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetById_NonExistingEntity_Returns404()
    {
        var response = await Client.GetAsync($"{Route}/999999999");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
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
