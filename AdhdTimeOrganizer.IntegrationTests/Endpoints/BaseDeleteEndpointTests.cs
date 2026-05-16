using System.Net;
using System.Net.Http.Json;
using AdhdTimeOrganizer.domain.model.entity.activity;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using FluentAssertions;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Endpoints;

public class BaseDeleteEndpointTests(TestWebApplicationFactory factory) : IntegrationTestBase(factory)
{
    private const string Route = "activity-category";

    private async Task<long> SeedCategoryAsync()
    {
        var db = CreateDbContext();
        var userId = await GetTestUserIdAsync();
        var category = new ActivityCategory { Name = "Delete Test", Color = "#FF0000", UserId = userId };
        db.ActivityCategories.Add(category);
        await db.SaveChangesAsync();
        return category.Id;
    }

    [Fact]
    public async Task Delete_ExistingEntity_Returns204()
    {
        var id = await SeedCategoryAsync();

        var response = await client.DeleteAsync($"{Route}/{id}");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Delete_WithoutAuth_Returns401()
    {
        

        var response = await anonClient.DeleteAsync($"{Route}/1");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Delete_NonExistingEntity_Returns404()
    {
        var response = await client.DeleteAsync($"{Route}/999999999");

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
