using System.Net;
using System.Net.Http.Json;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using FluentAssertions;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Endpoints;

public class BaseCreateEndpointTests(TestWebApplicationFactory factory) : IntegrationTestBase(factory)
{
    private const string Route = "/api/activity-category";

    [Fact]
    public async Task Create_ValidRequest_Returns201WithId()
    {
        var request = new { Name = "Test Category", Color = "#FF0000", Text = "desc" };

        var response = await Client.PostAsJsonAsync(Route, request);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var id = await response.Content.ReadFromJsonAsync<long>();
        id.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Create_WithoutAuth_Returns401()
    {
        using var anonClient = Factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            HandleCookies = false,
            AllowAutoRedirect = false
        });
        var request = new { Name = "Test", Color = "#FF0000" };

        var response = await anonClient.PostAsJsonAsync(Route, request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Create_MissingRequiredField_Returns400()
    {
        var request = new { Name = (string?)null, Color = "#FF0000" };

        var response = await Client.PostAsJsonAsync(Route, request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
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
