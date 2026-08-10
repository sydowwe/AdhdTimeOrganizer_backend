using AdhdTimeOrganizer.domain.model.entity;
using AdhdTimeOrganizer.Core.domain.model.@enum;
using AdhdTimeOrganizer.infrastructure.persistence;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using AdhdTimeOrganizer.Planning.domain.model.entity;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.Testing;
using Sydowwe.Framework.Testing.baseTests;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Endpoints;

// Tests BaseFilterEndpoint via FilterCalendarEndpoint ("/calendar/filter")
[Collection("Postgres")]
public class FilterCalendarEndpointTests(AppDbContextFixture fixture)
    : BaseFilterEndpointTests(fixture)
{
    protected override string EndpointUrl => "api/calendar/filter";

    protected override async Task<long> SeedEntityAsync(DbContext db)
    {
        var calendar = new Calendar
        {
            Date = DateOnly.FromDateTime(DateTime.Today),
            DayType = DayType.Workday,
            WakeUpTime = new TimeOnly(7, 0),
            BedTime = new TimeOnly(23, 0),
            UserId = FakeLoggedUserService.TestUserId
        };
        ((AppDbContext)db).Calendars.Add(calendar);
        await db.SaveChangesAsync();
        return calendar.Id;
    }

    // CalendarFilter.From/Until are -- the base's empty-object default would 400.
    protected override object EmptyFilterPayload() =>
        new
        {
            From = DateOnly.FromDateTime(DateTime.Today.AddDays(-1)),
            Until = DateOnly.FromDateTime(DateTime.Today.AddDays(1))
        };
}