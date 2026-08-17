using System.Net;
using System.Net.Http.Json;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using AdhdTimeOrganizer.Planning.application.dto.response.taskPlanner;
using AdhdTimeOrganizer.Planning.domain.model.entity.activityPlanning;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.Testing;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Endpoints;

/// <summary>
/// TEST-3 / Section E — <c>UserPlannerSettings</c> is one row per user, created lazily on first read/write
/// rather than seeded, and its <c>ReminderMinutesBefore</c> supplies the lead-offset default for a task-linked
/// reminder that omits one (deep coverage of that path lives in <c>ReminderRegistrationTests</c>; this pins
/// the settings endpoints' own contract).
/// </summary>
[Collection("Postgres")]
public class UserPlannerSettingsTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    private const long UserId = FakeLoggedUserService.TestUserId;

    [Fact(DisplayName = "A user with no settings row gets sane defaults, not a 500")]
    public async Task MissingRow_ReturnsDefaults()
    {
        await using (var db = CreateDbContext())
            (await db.Set<UserPlannerSettings>().AnyAsync(s => s.UserId == UserId, CancellationToken)).Should().BeFalse();

        var response = await CreateClient().GetAsync("api/planner/settings", CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var settings = await response.Content.ReadFromJsonAsync<UserPlannerSettingsResponse>(JsonOpts, CancellationToken);
        settings!.RemindersEnabled.Should().BeTrue();
        settings.ReminderMinutesBefore.Should().Be(10);
        settings.SlotDurationMinutes.Should().Be(10);
    }

    [Fact(DisplayName = "GET creates exactly one row even when called repeatedly")]
    public async Task RepeatedGet_CreatesExactlyOneRow()
    {
        var client = CreateClient();
        (await client.GetAsync("api/planner/settings", CancellationToken)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.GetAsync("api/planner/settings", CancellationToken)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.GetAsync("api/planner/settings", CancellationToken)).StatusCode.Should().Be(HttpStatusCode.OK);

        await using var db = CreateDbContext();
        (await db.Set<UserPlannerSettings>().CountAsync(s => s.UserId == UserId, CancellationToken)).Should().Be(1);
    }

    [Fact(DisplayName = "A user cannot end up with two settings rows via GET then PUT")]
    public async Task GetThenPut_StillOnlyOneRow()
    {
        var client = CreateClient();
        (await client.GetAsync("api/planner/settings", CancellationToken)).StatusCode.Should().Be(HttpStatusCode.OK);

        var update = await client.PutAsJsonAsync("api/planner/settings", ValidSettingsBody(), JsonOpts, CancellationToken);
        update.StatusCode.Should().Be(HttpStatusCode.NoContent);

        await using var db = CreateDbContext();
        (await db.Set<UserPlannerSettings>().CountAsync(s => s.UserId == UserId, CancellationToken)).Should().Be(1);
    }

    [Fact(DisplayName = "PUT with no prior row creates it (upsert), and a second PUT updates the same row")]
    public async Task PutWithNoPriorRow_CreatesThenUpdatesInPlace()
    {
        var client = CreateClient();
        var first = await client.PutAsJsonAsync("api/planner/settings", ValidSettingsBody(reminderMinutesBefore: 15), JsonOpts, CancellationToken);
        first.StatusCode.Should().Be(HttpStatusCode.NoContent);

        long idAfterFirst;
        await using (var db = CreateDbContext())
        {
            var row = await db.Set<UserPlannerSettings>().SingleAsync(s => s.UserId == UserId, CancellationToken);
            idAfterFirst = row.Id;
            row.ReminderMinutesBefore.Should().Be(15);
        }

        var second = await client.PutAsJsonAsync("api/planner/settings", ValidSettingsBody(reminderMinutesBefore: 45), JsonOpts, CancellationToken);
        second.StatusCode.Should().Be(HttpStatusCode.NoContent);

        await using var verifyDb = CreateDbContext();
        var rows = await verifyDb.Set<UserPlannerSettings>().Where(s => s.UserId == UserId).ToListAsync(CancellationToken);
        rows.Should().ContainSingle();
        rows[0].Id.Should().Be(idAfterFirst, "the second PUT updates the same row rather than inserting a second one");
        rows[0].ReminderMinutesBefore.Should().Be(45);
    }

    private static object ValidSettingsBody(int reminderMinutesBefore = 10) => new
    {
        RemindersEnabled = true,
        ReminderMinutesBefore = reminderMinutesBefore,
        DetailsPanelExpandedByDefault = true,
        ArrowKeyNavEnabled = true,
        PredefinedSkipReasons = new[] { "Not needed today" },
        SlotDurationMinutes = 15,
        DefaultConflictResolution = "Ignore",
        DefaultApplyPreviewMode = true
    };
}
