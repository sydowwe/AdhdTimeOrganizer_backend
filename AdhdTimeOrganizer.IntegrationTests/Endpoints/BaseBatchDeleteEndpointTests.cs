using System.Net;
using System.Net.Http.Json;
using AdhdTimeOrganizer.domain.model.entity.activity;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using FluentAssertions;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Endpoints;

public class BaseBatchDeleteEndpointTests(TestWebApplicationFactory factory) : IntegrationTestBase(factory)
{
    private const string Route = "/api/activity-category/batch-delete";

    private async Task<List<long>> SeedCategoriesAsync(int count)
    {
        var db = CreateDbContext();
        var userId = await GetTestUserIdAsync();
        var categories = Enumerable.Range(1, count)
            .Select(i => new ActivityCategory { Name = $"Batch {i}", Color = "#FF0000", UserId = userId })
            .ToList();
        db.ActivityCategories.AddRange(categories);
        await db.SaveChangesAsync();
        return categories.Select(c => c.Id).ToList();
    }

    [Fact]
    public async Task BatchDelete_ExistingEntities_Returns204()
    {
        var ids = await SeedCategoriesAsync(3);
        var request = new { Ids = ids };

        var response = await Client.PostAsJsonAsync(Route, request);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task BatchDelete_WithoutAuth_Returns401()
    {
        using var anonClient = Factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            HandleCookies = false,
            AllowAutoRedirect = false
        });

        var response = await anonClient.PostAsJsonAsync(Route, new { Ids = new[] { 1L } });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task BatchDelete_NonExistingEntity_Returns404()
    {
        var response = await Client.PostAsJsonAsync(Route, new { Ids = new[] { 999999999L } });

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
