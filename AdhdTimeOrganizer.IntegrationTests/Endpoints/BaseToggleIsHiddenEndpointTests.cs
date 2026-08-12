using AdhdTimeOrganizer.infrastructure.persistence;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using AdhdTimeOrganizer.Routines.domain.model.entity.todoList;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.Testing;
using Sydowwe.Framework.Testing.baseTests;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Endpoints;

// Tests BaseToggleIsHiddenEndpoint via ToggleIsHiddenRoutineTimePeriodEndpoint ("/routine-time-period/toggle-is-hidden")
[Collection("Postgres")]
public class ToggleIsHiddenRoutineTimePeriodEndpointTests(AppDbContextFixture fixture)
    : BaseToggleIsHiddenEndpointTests(fixture)
{
    protected override string EndpointUrl => "api/routine-time-period/toggle-is-hidden";

    // RoutineTimePeriod is user-owned data; the endpoint does not override AllowedRoles(), so plain
    // Users can reach it.
    protected override bool IsAdminOnly => false;

    protected override async Task<long> SeedEntityAsync(DbContext db)
    {
        var period = new RoutineTimePeriod
        {
            Text = "Test Period",
            Color = "#000000",
            UserId = FakeLoggedUserService.TestUserId,
            LengthInDays = 7,
            ResetAnchorDay = 1,
            StreakThreshold = 5,
            StreakGraceDays = 1
        };
        ((AppDbContext)db).RoutineTimePeriods.Add(period);
        await db.SaveChangesAsync();
        return period.Id;
    }
}