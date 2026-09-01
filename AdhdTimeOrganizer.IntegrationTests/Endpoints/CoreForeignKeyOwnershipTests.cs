using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AdhdTimeOrganizer.Core.domain.model.entity.activity;
using AdhdTimeOrganizer.Core.domain.model.entity.timer;
using AdhdTimeOrganizer.Core.domain.model.entity.user;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sydowwe.Framework.domain.@enum;
using Sydowwe.Framework.Testing;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Endpoints;

/// <summary>
/// SEC-2 — a caller-supplied foreign-key id has to name a row the caller can see.
/// </summary>
/// <remarks>
/// <para>
/// The per-user global query filter scopes <b>reads</b>. Every route below writes a raw <c>long</c>
/// into an FK column whose target row genuinely exists, so the constraint is satisfied and nothing
/// rejected it: <c>TimerPresetCrudAuthMatrixTests</c> and <c>ActivityEndpointTests</c> prove you cannot
/// read another user's row, never that you cannot point at one.
/// </para>
/// <para>
/// The answer is 404, not 403 — matching <c>MergeActivityEndpoint</c>, so a foreign id stays
/// indistinguishable from one that does not exist and another account's ids are not enumerable.
/// </para>
/// <para>
/// Assertions read rows back with <c>.IgnoreQueryFilters()</c>: the failure mode was a 201 over a row
/// the caller can never see again, so the status code alone proves nothing.
/// </para>
/// </remarks>
[Collection("Postgres")]
public class CoreForeignKeyOwnershipTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    private static long UserId => FakeLoggedUserService.TestUserId;

    [Fact(DisplayName = "SEC-2: an activity cannot be filed under another user's role")]
    public async Task CreateActivity_WithForeignRoleId_Is404AndWritesNothing()
    {
        long foreignRoleId;
        await using (var db = CreateDbContext())
        {
            var otherUserId = await SeedSecondUserAsync($"core-fk-role-{Guid.NewGuid():N}@test.com");
            foreignRoleId = await SeedRoleAsync(db, otherUserId, "Their Role");
        }

        var response = await CreateClient().PostAsJsonAsync("api/activity",
            // ActivityValidator caps Name at 50 characters and the suffix alone is 32.
            new { Name = $"Foreign role {Guid.NewGuid():N}", IsUnavoidable = false, RoleId = foreignRoleId },
            JsonOpts, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "the role is not visible to the caller, and a 403 would confirm it exists");

        await using var assertDb = CreateDbContext();
        (await assertDb.Set<Activity>().IgnoreQueryFilters().AnyAsync(a => a.RoleId == foreignRoleId, CancellationToken))
            .Should().BeFalse("the grid projects e.Role.Name through a user-filtered join, so such a row "
                              + "would exist, be the caller's, and be unreachable through the UI");
    }

    [Fact(DisplayName = "SEC-2: an activity cannot be classified with another user's category")]
    public async Task CreateActivity_WithForeignCategoryId_Is404AndWritesNothing()
    {
        long ownRoleId, foreignCategoryId;
        await using (var db = CreateDbContext())
        {
            var otherUserId = await SeedSecondUserAsync($"core-fk-category-{Guid.NewGuid():N}@test.com");
            ownRoleId = await SeedRoleAsync(db, UserId, "Own Role");
            foreignCategoryId = await SeedCategoryAsync(db, otherUserId, "Their Category");
        }

        var response = await CreateClient().PostAsJsonAsync("api/activity",
            new
            {
                Name = $"Foreign cat {Guid.NewGuid():N}", IsUnavoidable = false,
                RoleId = ownRoleId, CategoryId = foreignCategoryId
            },
            JsonOpts, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);

        await using var assertDb = CreateDbContext();
        (await assertDb.Set<Activity>().IgnoreQueryFilters()
            .AnyAsync(a => a.CategoryId == foreignCategoryId, CancellationToken))
            .Should().BeFalse("the optional FK is no less foreign for being optional");
    }

    [Fact(DisplayName = "SEC-2: quick-edit cannot repoint an activity at another user's category")]
    public async Task QuickEditActivity_WithForeignCategoryId_Is404AndLeavesTheRowAlone()
    {
        long activityId, foreignCategoryId;
        await using (var db = CreateDbContext())
        {
            var otherUserId = await SeedSecondUserAsync($"core-fk-quickedit-{Guid.NewGuid():N}@test.com");
            activityId = await SeedActivityAsync(db, UserId, "Quick Edit Mine");
            foreignCategoryId = await SeedCategoryAsync(db, otherUserId, "Their Quick Edit Category");
        }

        var response = await CreateClient().PutAsJsonAsync($"api/activity/{activityId}/quick-edit",
            new { Name = "Renamed", Text = (string?)null, CategoryId = foreignCategoryId }, JsonOpts, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);

        await using var assertDb = CreateDbContext();
        var activity = await assertDb.Set<Activity>().IgnoreQueryFilters()
            .FirstAsync(a => a.Id == activityId, CancellationToken);

        activity.CategoryId.Should().BeNull("the whole request is refused, so the rename must not land either");
        activity.Name.Should().NotBe("Renamed");
    }

    [Fact(DisplayName = "SEC-2: clone cannot classify the copy with another user's category")]
    public async Task CloneActivity_WithForeignCategoryId_Is404AndWritesNothing()
    {
        long activityId, foreignCategoryId;
        await using (var db = CreateDbContext())
        {
            var otherUserId = await SeedSecondUserAsync($"core-fk-clone-{Guid.NewGuid():N}@test.com");
            activityId = await SeedActivityAsync(db, UserId, "Clone Source");
            foreignCategoryId = await SeedCategoryAsync(db, otherUserId, "Their Clone Category");
        }

        var response = await CreateClient().PostAsJsonAsync($"api/activity/{activityId}/clone",
            new { Name = $"Clone Target {Guid.NewGuid():N}", Text = (string?)null, CategoryId = foreignCategoryId },
            JsonOpts, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);

        await using var assertDb = CreateDbContext();
        (await assertDb.Set<Activity>().IgnoreQueryFilters()
            .AnyAsync(a => a.CategoryId == foreignCategoryId, CancellationToken))
            .Should().BeFalse("the clone shares the quick-edit form and therefore the same unchecked id");
    }

    [Fact(DisplayName = "SEC-2: a timer preset cannot point at another user's activity")]
    public async Task CreateTimerPreset_WithForeignActivityId_Is404AndWritesNothing()
    {
        long foreignActivityId;
        await using (var db = CreateDbContext())
        {
            var otherUserId = await SeedSecondUserAsync($"core-fk-preset-{Guid.NewGuid():N}@test.com");
            foreignActivityId = await SeedActivityAsync(db, otherUserId, "Their Activity");
        }

        var response = await CreateClient().PostAsJsonAsync("api/timer-preset",
            new { Duration = 25, ActivityId = foreignActivityId }, JsonOpts, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);

        await using var assertDb = CreateDbContext();
        (await assertDb.Set<TimerPreset>().IgnoreQueryFilters()
            .AnyAsync(p => p.ActivityId == foreignActivityId, CancellationToken))
            .Should().BeFalse();
    }

    [Theory(DisplayName = "SEC-2: neither pomodoro activity column accepts another user's activity")]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CreatePomodoroTimerPreset_WithForeignActivityId_Is404AndWritesNothing(bool foreignIsFocus)
    {
        long ownActivityId, foreignActivityId;
        await using (var db = CreateDbContext())
        {
            var otherUserId = await SeedSecondUserAsync($"core-fk-pomodoro-{Guid.NewGuid():N}@test.com");
            ownActivityId = await SeedActivityAsync(db, UserId, "Own Pomodoro Activity");
            foreignActivityId = await SeedActivityAsync(db, otherUserId, "Their Pomodoro Activity");
        }

        var response = await CreateClient().PostAsJsonAsync("api/pomodoro-timer-preset",
            new
            {
                Name = $"Pomodoro {Guid.NewGuid():N}",
                FocusDuration = 25, ShortBreakDuration = 5, LongBreakDuration = 15,
                FocusPeriodInCycleCount = 4, NumberOfCycles = 1,
                FocusActivityId = foreignIsFocus ? foreignActivityId : ownActivityId,
                RestActivityId = foreignIsFocus ? ownActivityId : foreignActivityId
            },
            JsonOpts, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "both columns carry the same cascading FK, so both need the same guard");

        await using var assertDb = CreateDbContext();
        (await assertDb.Set<PomodoroTimerPreset>().IgnoreQueryFilters()
            .AnyAsync(p => p.FocusActivityId == foreignActivityId || p.RestActivityId == foreignActivityId,
                CancellationToken))
            .Should().BeFalse();
    }

    /// <summary>
    /// The reason the finding matters, asserted end to end. The <c>usageCount</c> / <c>canDelete</c>
    /// assertions stay even though the create is now refused: they document <em>why</em> a foreign
    /// reference is unacceptable — B's reference count cannot see A's preset, so B's delete button
    /// promises a safety it could not deliver.
    /// </summary>
    [Fact(DisplayName = "SEC-2: user B deleting their activity cannot destroy user A's preset")]
    public async Task ForeignPreset_CannotBeCreated_SoTheCrossUserCascadeIsUnreachable()
    {
        long otherUserId, theirActivityId;
        await using (var db = CreateDbContext())
        {
            otherUserId = await SeedSecondUserAsync($"core-fk-cascade-{Guid.NewGuid():N}@test.com");
            theirActivityId = await SeedActivityAsync(db, otherUserId, "Their Cascading Activity");
        }

        var createResponse = await CreateClient().PostAsJsonAsync("api/timer-preset",
            new { Duration = 25, ActivityId = theirActivityId }, JsonOpts, CancellationToken);

        createResponse.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "this is the step that used to succeed and put A's row behind B's delete button");

        await using var factoryB = CreateFactory(["User"], otherUserId);
        var clientB = factoryB.CreateClient();

        using var doc = JsonDocument.Parse(
            await clientB.GetStringAsync($"api/activity/{theirActivityId}", CancellationToken));
        doc.RootElement.GetProperty("usageCount").GetInt32().Should().Be(0,
            "B's reference count can only see B's own rows — which is exactly why A must never get a row in here");
        doc.RootElement.GetProperty("canDelete").GetBoolean().Should().BeTrue();

        (await clientB.DeleteAsync($"api/activity/{theirActivityId}", CancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        await using var assertDb = CreateDbContext();
        (await assertDb.Set<TimerPreset>().IgnoreQueryFilters()
            .AnyAsync(p => p.ActivityId == theirActivityId, CancellationToken))
            .Should().BeFalse("nothing of A's was ever allowed to point at it, so the cascade has nothing to take");
    }

    // ---- seeding ----------------------------------------------------------------------------------

    private async Task<long> SeedSecondUserAsync(string email)
    {
        using var scope = Fixture.UnauthenticatedFactory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var user = new User
        {
            Email = email, UserName = email,
            NormalizedEmail = email.ToUpperInvariant(), NormalizedUserName = email.ToUpperInvariant(),
            EmailConfirmed = true, Locale = AvailableLocales.En, Timezone = TimeZoneInfo.Utc
        };
        (await userManager.CreateAsync(user, "Test@1234!")).Succeeded.Should().BeTrue();
        return user.Id;
    }

    private async Task<long> SeedRoleAsync(DbContext db, long userId, string name)
    {
        var role = new ActivityRole { UserId = userId, Name = $"{name} {Guid.NewGuid():N}", Color = "#112233" };
        db.Set<ActivityRole>().Add(role);
        await db.SaveChangesAsync(CancellationToken);
        return role.Id;
    }

    private async Task<long> SeedCategoryAsync(DbContext db, long userId, string name)
    {
        var category = new ActivityCategory { UserId = userId, Name = $"{name} {Guid.NewGuid():N}", Color = "#445566" };
        db.Set<ActivityCategory>().Add(category);
        await db.SaveChangesAsync(CancellationToken);
        return category.Id;
    }

    private async Task<long> SeedActivityAsync(DbContext db, long userId, string name)
    {
        var activity = new Activity
        {
            UserId = userId,
            Name = $"{name} {Guid.NewGuid():N}",
            RoleId = await SeedRoleAsync(db, userId, $"{name} Own Role")
        };
        db.Set<Activity>().Add(activity);
        await db.SaveChangesAsync(CancellationToken);
        return activity.Id;
    }
}
