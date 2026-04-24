using System.Net;
using System.Net.Http.Json;
using AdhdTimeOrganizer.application.dto.response;
using AdhdTimeOrganizer.application.dto.response.@base;
using AdhdTimeOrganizer.domain.model.entity.activity;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using FluentAssertions;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Endpoints;

public class BaseFetchTableEndpointTests(TestWebApplicationFactory factory) : IntegrationTestBase(factory)
{
    private const string Route = "/api/activity-category/filtered-table";

    private async Task SeedCategoriesAsync()
    {
        var db = CreateDbContext();
        var userId = await GetTestUserIdAsync();
        db.ActivityCategories.AddRange(
            new ActivityCategory { Name = "Table A", Color = "#FF0000", UserId = userId },
            new ActivityCategory { Name = "Table B", Color = "#00FF00", UserId = userId });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task FetchTable_ValidRequest_Returns200WithPage()
    {
        await SeedCategoriesAsync();
        var request = new
        {
            ItemsPerPage = 10,
            Page = 1,
            SortBy = Array.Empty<object>(),
            UseFilter = false,
            Filter = new { }
        };

        var response = await Client.PostAsJsonAsync(Route, request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var table = await response.Content.ReadFromJsonAsync<BaseTableResponse<ActivityCategoryResponse>>();
        table.Should().NotBeNull();
        table!.Items.Should().NotBeNull();
        table.ItemsCount.Should().BeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public async Task FetchTable_WithoutAuth_Returns401()
    {
        using var anonClient = Factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            HandleCookies = false,
            AllowAutoRedirect = false
        });

        var response = await anonClient.PostAsJsonAsync(Route, new { ItemsPerPage = 10, Page = 1, SortBy = Array.Empty<object>(), UseFilter = false, Filter = new { } });

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
