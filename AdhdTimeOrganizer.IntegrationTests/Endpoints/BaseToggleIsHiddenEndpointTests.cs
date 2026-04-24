using System.Net;
using System.Net.Http.Json;
using AdhdTimeOrganizer.domain.model.entity.todoList;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using FluentAssertions;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Endpoints;

// Tests BaseToggleIsHiddenEndpoint via ToggleIsHiddenRoutineTimePeriodEndpoint
public class BaseToggleIsHiddenEndpointTests(TestWebApplicationFactory factory) : IntegrationTestBase(factory)
{
    private const string Route = "/api/routine-time-period/toggle-is-hidden";

    private async Task<long> SeedRoutineTimePeriodAsync()
    {
        var db = CreateDbContext();
        var userId = await GetTestUserIdAsync();
        var period = new RoutineTimePeriod
        {
            Text = "Test Period",
            UserId = userId,
            LengthInDays = 7,
            ResetAnchorDay = 1,
            StreakThreshold = 5,
            StreakGraceDays = 1
        };
        db.RoutineTimePeriods.Add(period);
        await db.SaveChangesAsync();
        return period.Id;
    }

    [Fact]
    public async Task ToggleIsHidden_ExistingEntity_Returns204()
    {
        var id = await SeedRoutineTimePeriodAsync();
        var request = new { Ids = new[] { id } };

        var response = await Client.PatchAsJsonAsync(Route, request);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task ToggleIsHidden_WithoutAuth_Returns401()
    {
        using var anonClient = Factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            HandleCookies = false,
            AllowAutoRedirect = false
        });

        var response = await anonClient.PatchAsJsonAsync(Route, new { Ids = new[] { 1L } });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ToggleIsHidden_NonExistingEntity_Returns404()
    {
        var response = await Client.PatchAsJsonAsync(Route, new { Ids = new[] { 999999999L } });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    public override async Task DisposeAsync()
    {
        var db = CreateDbContext();
        var userId = await GetTestUserIdAsync();
        db.RoutineTimePeriods.RemoveRange(
            db.RoutineTimePeriods.Where(p => p.UserId == userId));
        await db.SaveChangesAsync();
    }
}
