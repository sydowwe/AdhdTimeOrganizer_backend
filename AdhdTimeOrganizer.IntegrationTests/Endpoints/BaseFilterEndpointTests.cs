using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AdhdTimeOrganizer.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.domain.model.@enum;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using FluentAssertions;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Endpoints;

// Tests BaseFilterEndpoint via GetFilteredSortedCalendar
public class BaseFilterEndpointTests(TestWebApplicationFactory factory) : IntegrationTestBase(factory)
{
    private const string Route = "/api/calendar/filter";

    private async Task SeedCalendarAsync()
    {
        var db = CreateDbContext();
        var userId = await GetTestUserIdAsync();
        db.Calendars.Add(new Calendar
        {
            Date = DateOnly.FromDateTime(DateTime.Today),
            DayType = DayType.Workday,
            WakeUpTime = new TimeOnly(7, 0),
            BedTime = new TimeOnly(23, 0),
            UserId = userId
        });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Filter_ValidRequest_Returns200WithList()
    {
        await SeedCalendarAsync();
        var request = new
        {
            From = DateOnly.FromDateTime(DateTime.Today.AddDays(-1)).ToString("yyyy-MM-dd"),
            Until = DateOnly.FromDateTime(DateTime.Today.AddDays(1)).ToString("yyyy-MM-dd")
        };

        var response = await Client.PostAsJsonAsync(Route, request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        doc.RootElement.GetArrayLength().Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task Filter_WithoutAuth_Returns401()
    {
        using var anonClient = Factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            HandleCookies = false,
            AllowAutoRedirect = false
        });
        var request = new
        {
            From = "2024-01-01",
            Until = "2024-12-31"
        };

        var response = await anonClient.PostAsJsonAsync(Route, request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    public override async Task DisposeAsync()
    {
        var db = CreateDbContext();
        var userId = await GetTestUserIdAsync();
        db.Calendars.RemoveRange(
            db.Calendars.Where(c => c.UserId == userId));
        await db.SaveChangesAsync();
    }
}
