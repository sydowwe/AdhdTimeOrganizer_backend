using System.Net;
using System.Net.Http.Json;
using AdhdTimeOrganizer.domain.model.entity.activity;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using FluentAssertions;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Endpoints;

public class BaseUpdateEndpointTests(TestWebApplicationFactory factory) : IntegrationTestBase(factory)
{
    private const string Route = "/api/activity-category";

    private async Task<long> SeedCategoryAsync()
    {
        var db = CreateDbContext();
        var userId = await GetTestUserIdAsync();
        var category = new ActivityCategory
        {
            Name = "Update Test",
            Color = "#0000FF",
            UserId = userId
        };
        db.ActivityCategories.Add(category);
        await db.SaveChangesAsync();
        return category.Id;
    }

    [Fact]
    public async Task Update_ValidRequest_Returns204()
    {
        var id = await SeedCategoryAsync();
        var request = new { Name = "Updated", Color = "#00FF00", Text = "updated" };

        var response = await Client.PutAsJsonAsync($"{Route}/{id}", request);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Update_WithoutAuth_Returns401()
    {
        using var anonClient = Factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            HandleCookies = false,
            AllowAutoRedirect = false
        });

        var response = await anonClient.PutAsJsonAsync($"{Route}/1", new { Name = "X", Color = "#000000" });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Update_NonExistingEntity_Returns404()
    {
        var response = await Client.PutAsJsonAsync($"{Route}/999999999", new { Name = "X", Color = "#000000" });

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
