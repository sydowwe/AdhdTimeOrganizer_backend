using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AdhdTimeOrganizer.Core.application.dto.response.timer;
using AdhdTimeOrganizer.Core.domain.model.entity.activity;
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
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Endpoints;

/// <summary>
/// TEST-10 -- business-logic/correctness coverage for TimerPreset and PomodoroTimerPreset, layered on
/// top of TEST-1's auth matrix (TimerPresetCrudAuthMatrixTests.cs) and the validation edge cases in
/// TimerPresetValidationTests.cs. Neither preset entity references the other -- each independently
/// holds an optional cascade FK to Activity (TimerPresetConfiguration / PomodoroTimerPresetConfiguration
/// both use OnDelete(DeleteBehavior.Cascade)), so the delete-behavior worth pinning here is
/// Activity -> preset, not preset -> preset. UpdateEntity on both request DTOs unconditionally
/// overwrites every property (no partial-patch support), so Update tests assert full replacement.
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

    public static async Task<long> RoleAsync(DbContext db, long userId, string name)
    {
        var role = new ActivityRole { UserId = userId, Name = name, Color = "#112233" };
        db.Set<ActivityRole>().Add(role);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return role.Id;
    }

    public static async Task<long> ActivityAsync(DbContext db, long userId, string name)
    {
        var roleId = await RoleAsync(db, userId, $"{name} Role {Guid.NewGuid():N}");
        var activity = new Activity { UserId = userId, Name = name, RoleId = roleId };
        db.Set<Activity>().Add(activity);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return activity.Id;
    }

    public static async Task<long> TimerPresetAsync(DbContext db, long userId, int duration = 25, long? activityId = null)
    {
        var preset = new TimerPreset { UserId = userId, Duration = duration, ActivityId = activityId };
        db.Set<TimerPreset>().Add(preset);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return preset.Id;
    }

    public static async Task<long> PomodoroTimerPresetAsync(DbContext db, long userId, string name = "Pomodoro Preset",
        long? focusActivityId = null, long? restActivityId = null)
    {
        var preset = new PomodoroTimerPreset
        {
            UserId = userId,
            Name = name,
            FocusDuration = 25,
            ShortBreakDuration = 5,
            LongBreakDuration = 15,
            FocusPeriodInCycleCount = 4,
            NumberOfCycles = 1,
            FocusActivityId = focusActivityId,
            RestActivityId = restActivityId
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
public class TimerPresetCrudTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    [Fact]
    public async Task Create_ThenGetById_ReturnsExactlyWhatWasCreated()
    {
        await using var db = CreateDbContext();
        var userId = FakeLoggedUserService.TestUserId;
        var activityId = await Seed.ActivityAsync(db, userId, "Deep Work");

        var client = CreateClient();
        var createResponse = await client.PostAsJsonAsync("api/timer-preset", new { Duration = 45, ActivityId = activityId });
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var id = await createResponse.Content.ReadFromJsonAsync<long>(cancellationToken: CancellationToken);

        var getResponse = await client.GetAsync($"api/timer-preset/{id}");
        getResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await getResponse.Content.ReadFromJsonAsync<TimerPresetResponse>(cancellationToken: CancellationToken);

        body.Should().NotBeNull();
        body!.Id.Should().Be(id);
        body.Duration.Should().Be(45);
        body.Activity.Should().NotBeNull();
        body.Activity!.Id.Should().Be(activityId);
        body.Activity.Name.Should().Be("Deep Work");
    }

    [Fact]
    public async Task Update_ReplacesDurationAndActivityId_FullOverwriteNotPartialPatch()
    {
        await using var db = CreateDbContext();
        var userId = FakeLoggedUserService.TestUserId;
        var activityId = await Seed.ActivityAsync(db, userId, "Original Activity");
        var id = await Seed.TimerPresetAsync(db, userId, duration: 25, activityId: activityId);

        var response = await CreateClient().PutAsJsonAsync($"api/timer-preset/{id}", new { Duration = 60, ActivityId = (long?)null });
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        await using var assertDb = CreateDbContext();
        var updated = await assertDb.Set<TimerPreset>().SingleAsync(t => t.Id == id, CancellationToken);
        updated.Duration.Should().Be(60);
        updated.ActivityId.Should().BeNull("Update is a full replace -- omitting ActivityId from the payload clears it, it does not leave the previous value untouched");
    }

    [Fact]
    public async Task DeletingActivity_CascadesToLinkedTimerPreset_NotRestrict()
    {
        await using var db = CreateDbContext();
        var userId = FakeLoggedUserService.TestUserId;
        var activityId = await Seed.ActivityAsync(db, userId, "Activity With TimerPreset");
        var presetId = await Seed.TimerPresetAsync(db, userId, activityId: activityId);

        var response = await CreateClient().DeleteAsync($"api/activity/{activityId}");
        response.StatusCode.Should().Be(HttpStatusCode.NoContent, "the FK is Cascade, not Restrict, so the delete must succeed");

        await using var assertDb = CreateDbContext();
        (await assertDb.Set<TimerPreset>().IgnoreQueryFilters().AnyAsync(t => t.Id == presetId, CancellationToken))
            .Should().BeFalse("the timer preset must be cascade-deleted along with its activity, not merely have ActivityId nulled out");
    }

    [Fact]
    public async Task GetAll_ReturnsOnlyCallersOwnPresets()
    {
        await using var db = CreateDbContext();
        var userId = FakeLoggedUserService.TestUserId;
        var firstId = await Seed.TimerPresetAsync(db, userId, duration: 15);
        var secondId = await Seed.TimerPresetAsync(db, userId, duration: 50);

        var otherUserId = await Seed.SecondUserAsync(Fixture, $"timerpreset-getall-{Guid.NewGuid():N}@test.com");
        var foreignId = await Seed.TimerPresetAsync(db, otherUserId, duration: 99);

        var response = await CreateClient().GetAsync("api/timer-preset");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var items = await response.Content.ReadFromJsonAsync<JsonElement[]>(cancellationToken: CancellationToken);

        items.Should().NotBeNull();
        var ids = items!.Select(i => i.GetProperty("id").GetInt64()).ToArray();
        ids.Should().Contain([firstId, secondId]).And.NotContain(foreignId);
    }
}

// =====================================================================================================
// PomodoroTimerPreset
// =====================================================================================================

[Collection("Postgres")]
public class PomodoroTimerPresetCrudTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    [Fact]
    public async Task Create_ThenGetById_ReturnsExactlyWhatWasCreated()
    {
        await using var db = CreateDbContext();
        var userId = FakeLoggedUserService.TestUserId;
        var focusActivityId = await Seed.ActivityAsync(db, userId, "Focus Activity");
        var restActivityId = await Seed.ActivityAsync(db, userId, "Rest Activity");

        var client = CreateClient();
        var createResponse = await client.PostAsJsonAsync("api/pomodoro-timer-preset", new
        {
            Name = "Created Pomodoro",
            FocusDuration = 25,
            ShortBreakDuration = 5,
            LongBreakDuration = 15,
            FocusPeriodInCycleCount = 4,
            NumberOfCycles = 1,
            FocusActivityId = focusActivityId,
            RestActivityId = restActivityId
        });
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var id = await createResponse.Content.ReadFromJsonAsync<long>(cancellationToken: CancellationToken);

        var getResponse = await client.GetAsync($"api/pomodoro-timer-preset/{id}");
        getResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await getResponse.Content.ReadFromJsonAsync<PomodoroTimerPresetResponse>(cancellationToken: CancellationToken);

        body.Should().NotBeNull();
        body!.Id.Should().Be(id);
        body.Name.Should().Be("Created Pomodoro");
        body.FocusDuration.Should().Be(25);
        body.ShortBreakDuration.Should().Be(5);
        body.LongBreakDuration.Should().Be(15);
        body.FocusPeriodInCycleCount.Should().Be(4);
        body.NumberOfCycles.Should().Be(1);
        body.FocusActivity.Should().NotBeNull();
        body.FocusActivity!.Id.Should().Be(focusActivityId);
        body.RestActivity.Should().NotBeNull();
        body.RestActivity!.Id.Should().Be(restActivityId);
    }

    [Fact]
    public async Task Update_ReplacesAllFields_FullOverwriteNotPartialPatch()
    {
        await using var db = CreateDbContext();
        var userId = FakeLoggedUserService.TestUserId;
        var focusActivityId = await Seed.ActivityAsync(db, userId, "Original Focus Activity");
        var id = await Seed.PomodoroTimerPresetAsync(db, userId, "Original Name", focusActivityId: focusActivityId);

        var response = await CreateClient().PutAsJsonAsync($"api/pomodoro-timer-preset/{id}", new
        {
            Name = "Updated Name",
            FocusDuration = 30,
            ShortBreakDuration = 10,
            LongBreakDuration = 20,
            FocusPeriodInCycleCount = 3,
            NumberOfCycles = 2,
            FocusActivityId = (long?)null,
            RestActivityId = (long?)null
        });
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        await using var assertDb = CreateDbContext();
        var updated = await assertDb.Set<PomodoroTimerPreset>().SingleAsync(p => p.Id == id, CancellationToken);
        updated.Name.Should().Be("Updated Name");
        updated.FocusDuration.Should().Be(30);
        updated.ShortBreakDuration.Should().Be(10);
        updated.LongBreakDuration.Should().Be(20);
        updated.FocusPeriodInCycleCount.Should().Be(3);
        updated.NumberOfCycles.Should().Be(2);
        updated.FocusActivityId.Should().BeNull("Update is a full replace -- omitting FocusActivityId from the payload clears it");
    }

    [Fact]
    public async Task DeletingFocusActivity_CascadesToLinkedPomodoroPreset_NotRestrict()
    {
        await using var db = CreateDbContext();
        var userId = FakeLoggedUserService.TestUserId;
        var focusActivityId = await Seed.ActivityAsync(db, userId, "Focus Activity With Pomodoro");
        var restActivityId = await Seed.ActivityAsync(db, userId, "Rest Activity With Pomodoro");
        var presetId = await Seed.PomodoroTimerPresetAsync(db, userId, focusActivityId: focusActivityId, restActivityId: restActivityId);

        var response = await CreateClient().DeleteAsync($"api/activity/{focusActivityId}");
        response.StatusCode.Should().Be(HttpStatusCode.NoContent, "the FK is Cascade, not Restrict, so the delete must succeed");

        await using var assertDb = CreateDbContext();
        (await assertDb.Set<PomodoroTimerPreset>().IgnoreQueryFilters().AnyAsync(p => p.Id == presetId, CancellationToken))
            .Should().BeFalse("the pomodoro preset must be cascade-deleted along with its focus activity, not merely have FocusActivityId nulled out");
    }

    [Fact]
    public async Task GetAll_ReturnsOnlyCallersOwnPresets()
    {
        await using var db = CreateDbContext();
        var userId = FakeLoggedUserService.TestUserId;
        var firstId = await Seed.PomodoroTimerPresetAsync(db, userId, "First Pomodoro");
        var secondId = await Seed.PomodoroTimerPresetAsync(db, userId, "Second Pomodoro");

        var otherUserId = await Seed.SecondUserAsync(Fixture, $"pomodoro-getall-{Guid.NewGuid():N}@test.com");
        var foreignId = await Seed.PomodoroTimerPresetAsync(db, otherUserId, "Foreign Pomodoro");

        var response = await CreateClient().GetAsync("api/pomodoro-timer-preset");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var items = await response.Content.ReadFromJsonAsync<JsonElement[]>(cancellationToken: CancellationToken);

        items.Should().NotBeNull();
        var ids = items!.Select(i => i.GetProperty("id").GetInt64()).ToArray();
        ids.Should().Contain([firstId, secondId]).And.NotContain(foreignId);
    }
}
