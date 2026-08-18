using System.Net;
using AdhdTimeOrganizer.Core.domain.model.entity.timer;
using AdhdTimeOrganizer.Core.domain.model.entity.user;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sydowwe.Framework.domain.entity.user;
using Sydowwe.Framework.domain.@enum;
using Sydowwe.Framework.Testing;
using Sydowwe.Framework.Testing.baseTests;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Endpoints;

/// <summary>
/// TEST-1 -- TimerPreset and PomodoroTimerPreset are both IEntityWithUser (like Activity, see
/// ActivityEndpointTests.cs), so a cross-user id is filtered out by AppDbContext's global query
/// filter before any AuthorizeAsync hook runs -- every UnauthorizedStatus override below is NotFound,
/// not the bases' default Forbidden. Neither entity exposes Filter/FetchTable/GetSelectOptions/
/// BatchDelete endpoints, only Create/Update/Delete/GetById/GetAll.
/// </summary>
file static class Seed
{
    public const string Password = "Test@1234!";

    public static async Task<long> SecondUserAsync(IPostgresFixture fixture, string email)
    {
        using var scope = fixture.UnauthenticatedFactory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var user = new User
        {
            Email = email,
            UserName = email,
            NormalizedEmail = email.ToUpperInvariant(),
            NormalizedUserName = email.ToUpperInvariant(),
            EmailConfirmed = true,
            Locale = AvailableLocales.En,
            Timezone = TimeZoneInfo.Utc
        };
        var result = await userManager.CreateAsync(user, Password);
        result.Succeeded.Should().BeTrue();
        return user.Id;
    }

    public static async Task<long> TimerPresetAsync(DbContext db, long userId, int duration = 25)
    {
        var preset = new TimerPreset { UserId = userId, Duration = duration };
        db.Set<TimerPreset>().Add(preset);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return preset.Id;
    }

    public static async Task<long> PomodoroTimerPresetAsync(DbContext db, long userId, string name = "Pomodoro Preset")
    {
        var preset = new PomodoroTimerPreset
        {
            UserId = userId,
            Name = name,
            FocusDuration = 25,
            ShortBreakDuration = 5,
            LongBreakDuration = 15,
            FocusPeriodInCycleCount = 4,
            NumberOfCycles = 1
        };
        db.Set<PomodoroTimerPreset>().Add(preset);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return preset.Id;
    }
}

// =====================================================================================================
// TimerPreset
// =====================================================================================================

[Collection("Postgres")]
public class TimerPresetGetByIdTests(AppDbContextFixture fixture) : BaseGetByIdEndpointTests(fixture)
{
    protected override string EndpointUrl => "api/timer-preset";
    protected override bool IsAdminOnly => false;
    protected override HttpStatusCode UnauthorizedStatus => HttpStatusCode.NotFound;
    protected override Task<long> SeedEntityAsync(DbContext db) => Seed.TimerPresetAsync(db, FakeLoggedUserService.TestUserId);

    protected override async Task<long?> SeedEntityOwnedByOtherUserAsync(DbContext db)
    {
        var otherUserId = await Seed.SecondUserAsync(Fixture, $"timerpreset-getbyid-{Guid.NewGuid():N}@test.com");
        return await Seed.TimerPresetAsync(db, otherUserId);
    }
}

[Collection("Postgres")]
public class TimerPresetGetAllTests(AppDbContextFixture fixture) : BaseGetAllEndpointTests(fixture)
{
    protected override string EndpointUrl => "api/timer-preset";
    protected override bool IsAdminOnly => false;
    protected override Task<long> SeedEntityAsync(DbContext db) => Seed.TimerPresetAsync(db, FakeLoggedUserService.TestUserId);
}

[Collection("Postgres")]
public class TimerPresetCreateTests(AppDbContextFixture fixture) : BaseCreateEndpointTests(fixture)
{
    protected override string EndpointUrl => "api/timer-preset";
    protected override bool IsAdminOnly => false;
    protected override Task<object> BuildValidPayloadAsync(DbContext db) =>
        Task.FromResult<object>(new { Duration = 25 });
}

[Collection("Postgres")]
public class TimerPresetUpdateTests(AppDbContextFixture fixture) : BaseUpdateEndpointTests(fixture)
{
    protected override string EndpointUrl => "api/timer-preset";
    protected override bool IsAdminOnly => false;
    protected override HttpStatusCode UnauthorizedStatus => HttpStatusCode.NotFound;
    protected override Task<long> SeedEntityAsync(DbContext db) => Seed.TimerPresetAsync(db, FakeLoggedUserService.TestUserId);
    protected override Task<object> BuildValidPayloadAsync(DbContext db, long id) =>
        Task.FromResult<object>(new { Duration = 50 });

    protected override async Task<long?> SeedEntityOwnedByOtherUserAsync(DbContext db)
    {
        var otherUserId = await Seed.SecondUserAsync(Fixture, $"timerpreset-update-{Guid.NewGuid():N}@test.com");
        return await Seed.TimerPresetAsync(db, otherUserId);
    }
}

[Collection("Postgres")]
public class TimerPresetDeleteTests(AppDbContextFixture fixture) : BaseDeleteEndpointTests(fixture)
{
    protected override string EndpointUrl => "api/timer-preset";
    protected override bool IsAdminOnly => false;
    protected override HttpStatusCode UnauthorizedStatus => HttpStatusCode.NotFound;
    protected override Task<long> SeedEntityAsync(DbContext db) => Seed.TimerPresetAsync(db, FakeLoggedUserService.TestUserId);

    protected override async Task<long?> SeedEntityOwnedByOtherUserAsync(DbContext db)
    {
        var otherUserId = await Seed.SecondUserAsync(Fixture, $"timerpreset-delete-{Guid.NewGuid():N}@test.com");
        return await Seed.TimerPresetAsync(db, otherUserId);
    }
}

// =====================================================================================================
// PomodoroTimerPreset
// =====================================================================================================

[Collection("Postgres")]
public class PomodoroTimerPresetGetByIdTests(AppDbContextFixture fixture) : BaseGetByIdEndpointTests(fixture)
{
    protected override string EndpointUrl => "api/pomodoro-timer-preset";
    protected override bool IsAdminOnly => false;
    protected override HttpStatusCode UnauthorizedStatus => HttpStatusCode.NotFound;
    protected override Task<long> SeedEntityAsync(DbContext db) => Seed.PomodoroTimerPresetAsync(db, FakeLoggedUserService.TestUserId, "GetById Pomodoro");

    protected override async Task<long?> SeedEntityOwnedByOtherUserAsync(DbContext db)
    {
        var otherUserId = await Seed.SecondUserAsync(Fixture, $"pomodoro-getbyid-{Guid.NewGuid():N}@test.com");
        return await Seed.PomodoroTimerPresetAsync(db, otherUserId, "Foreign Pomodoro");
    }
}

[Collection("Postgres")]
public class PomodoroTimerPresetGetAllTests(AppDbContextFixture fixture) : BaseGetAllEndpointTests(fixture)
{
    protected override string EndpointUrl => "api/pomodoro-timer-preset";
    protected override bool IsAdminOnly => false;
    protected override Task<long> SeedEntityAsync(DbContext db) => Seed.PomodoroTimerPresetAsync(db, FakeLoggedUserService.TestUserId, "GetAll Pomodoro");
}

[Collection("Postgres")]
public class PomodoroTimerPresetCreateTests(AppDbContextFixture fixture) : BaseCreateEndpointTests(fixture)
{
    protected override string EndpointUrl => "api/pomodoro-timer-preset";
    protected override bool IsAdminOnly => false;
    protected override Task<object> BuildValidPayloadAsync(DbContext db) =>
        Task.FromResult<object>(new
        {
            Name = $"Created Pomodoro {Guid.NewGuid():N}",
            FocusDuration = 25,
            ShortBreakDuration = 5,
            LongBreakDuration = 15,
            FocusPeriodInCycleCount = 4,
            NumberOfCycles = 1
        });
}

[Collection("Postgres")]
public class PomodoroTimerPresetUpdateTests(AppDbContextFixture fixture) : BaseUpdateEndpointTests(fixture)
{
    protected override string EndpointUrl => "api/pomodoro-timer-preset";
    protected override bool IsAdminOnly => false;
    protected override HttpStatusCode UnauthorizedStatus => HttpStatusCode.NotFound;
    protected override Task<long> SeedEntityAsync(DbContext db) => Seed.PomodoroTimerPresetAsync(db, FakeLoggedUserService.TestUserId, "Update Pomodoro");
    protected override Task<object> BuildValidPayloadAsync(DbContext db, long id) =>
        Task.FromResult<object>(new
        {
            Name = "Updated Pomodoro",
            FocusDuration = 30,
            ShortBreakDuration = 10,
            LongBreakDuration = 20,
            FocusPeriodInCycleCount = 3,
            NumberOfCycles = 2
        });

    protected override async Task<long?> SeedEntityOwnedByOtherUserAsync(DbContext db)
    {
        var otherUserId = await Seed.SecondUserAsync(Fixture, $"pomodoro-update-{Guid.NewGuid():N}@test.com");
        return await Seed.PomodoroTimerPresetAsync(db, otherUserId, "Foreign Update Pomodoro");
    }
}

[Collection("Postgres")]
public class PomodoroTimerPresetDeleteTests(AppDbContextFixture fixture) : BaseDeleteEndpointTests(fixture)
{
    protected override string EndpointUrl => "api/pomodoro-timer-preset";
    protected override bool IsAdminOnly => false;
    protected override HttpStatusCode UnauthorizedStatus => HttpStatusCode.NotFound;
    protected override Task<long> SeedEntityAsync(DbContext db) => Seed.PomodoroTimerPresetAsync(db, FakeLoggedUserService.TestUserId, "Delete Pomodoro");

    protected override async Task<long?> SeedEntityOwnedByOtherUserAsync(DbContext db)
    {
        var otherUserId = await Seed.SecondUserAsync(Fixture, $"pomodoro-delete-{Guid.NewGuid():N}@test.com");
        return await Seed.PomodoroTimerPresetAsync(db, otherUserId, "Foreign Delete Pomodoro");
    }
}
