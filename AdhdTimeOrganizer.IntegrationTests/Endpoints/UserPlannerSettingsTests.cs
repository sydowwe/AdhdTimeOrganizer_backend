using System.Net;
using System.Net.Http.Json;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using AdhdTimeOrganizer.Planning.application.dto.response.taskPlanner;
using AdhdTimeOrganizer.Planning.domain.model.entity.activityPlanning;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Sydowwe.Framework.Testing;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Endpoints;

/// <summary>
/// TEST-3 / Section E — <c>UserPlannerSettings</c> is one row per user, created lazily on first read/write
/// rather than seeded, and its <c>ReminderMinutesBefore</c> supplies the lead-offset default for a task-linked
/// reminder that omits one (deep coverage of that path lives in <c>ReminderRegistrationTests</c>; this pins
/// the settings endpoints' own contract).
/// <para>
/// SEC-3 added the four concurrency cases at the bottom: <c>user_id</c> is unique, so the row cannot double
/// — what a race produces is a loser holding a unique violation, and both endpoints now recover from it
/// rather than answering 500. The uniqueness the other three lean on is itself pinned, below HTTP, by
/// <c>TwoRowsForOneUser_AreRejectedByTheDatabase</c>.
/// </para>
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

    /// <summary>
    /// SEC-3 — the read is a get-or-create and <c>user_planner_settings.user_id</c> is unique, so two devices
    /// opening the planner for the first time at once cannot both insert: one takes a unique violation on a
    /// plain <c>GET</c> and used to answer 500.
    /// <para>
    /// Raced rather than simulated, for the reason <c>RoutineResetConcurrencyTests</c>' SEC-5 pair gives: the
    /// recovery branch needs the row absent at read time and present at insert time, which no seeding can
    /// arrange. In the rare run where the two do not overlap the test passes vacuously rather than flaking, so
    /// the assertion is "no 5xx and exactly one row" rather than "somebody lost".
    /// </para>
    /// </summary>
    [Fact(DisplayName = "Two first reads at once both get their settings, and leave one row")]
    public async Task ConcurrentFirstGet_BothSucceed_AndLeaveOneRow()
    {
        var reads = Enumerable.Range(0, 2)
            .Select(_ => CreateClient().GetAsync("api/planner/settings", CancellationToken))
            .ToList();

        var responses = await Task.WhenAll(reads);

        responses.Should().OnlyContain(r => r.StatusCode == HttpStatusCode.OK,
            "losing the insert race is not the reader's problem — the loser reads the winner's row");
        foreach (var response in responses)
        {
            var settings = await response.Content.ReadFromJsonAsync<UserPlannerSettingsResponse>(JsonOpts, CancellationToken);
            settings!.ReminderMinutesBefore.Should().Be(10, "both callers get real settings, not a half-built row");
        }

        await using var db = CreateDbContext();
        (await db.Set<UserPlannerSettings>().AsNoTracking()
            .CountAsync(s => s.UserId == UserId, CancellationToken))
            .Should().Be(1, "the unique index holds and nobody inserted a second row");
    }

    /// <summary>
    /// The write half of the same race. <c>PUT</c> is an upsert on the same row, so a first write concurrent
    /// with anything else that creates it collides on the same index — now on
    /// <c>BaseUserSettingsUpsertEndpoint</c>, which recovers onto the winner's row.
    /// </summary>
    [Fact(DisplayName = "Two first writes at once both succeed, and leave one row")]
    public async Task ConcurrentFirstPut_BothSucceed_AndLeaveOneRow()
    {
        var writes = Enumerable.Range(0, 2)
            .Select(i => CreateClient().PutAsJsonAsync("api/planner/settings",
                ValidSettingsBody(reminderMinutesBefore: 20 + i), JsonOpts, CancellationToken))
            .ToList();

        var responses = await Task.WhenAll(writes);

        responses.Should().OnlyContain(
            r => r.StatusCode == HttpStatusCode.NoContent || r.StatusCode == HttpStatusCode.Conflict,
            "a lost race is a 409 the client can retry, never a 500");

        await using var db = CreateDbContext();
        var rows = await db.Set<UserPlannerSettings>().AsNoTracking()
            .Where(s => s.UserId == UserId).ToListAsync(CancellationToken);
        rows.Should().ContainSingle("the unique index holds and nobody inserted a second row");
        rows[0].ReminderMinutesBefore.Should().BeOneOf([20, 21], "the row holds a value somebody actually sent");
    }

    /// <summary>
    /// SEC-3, deterministically. Both race tests above pass vacuously when the two callers do not overlap, so
    /// neither of them actually proves the constraint they lean on exists — they would stay green against a
    /// database whose <c>user_id</c> index was non-unique. This asserts the index directly: it is the thing
    /// that makes "recover and re-read" a correct fix rather than a narrowing of the window.
    /// <para>
    /// It also pins the uniqueness against the way it is configured. The Framework helper's
    /// <c>HasIndex(UserId)</c> is non-unique; the index is unique because
    /// <c>UserPlannerSettingsConfiguration</c> says so on top of it. Drop that line and this is the only test
    /// in the solution that fails.
    /// </para>
    /// </summary>
    [Fact(DisplayName = "user_planner_settings.user_id is unique — the row cannot double even below HTTP")]
    public async Task TwoRowsForOneUser_AreRejectedByTheDatabase()
    {
        await using var db = CreateDbContext();
        db.Set<UserPlannerSettings>().Add(new UserPlannerSettings { UserId = UserId });
        db.Set<UserPlannerSettings>().Add(new UserPlannerSettings { UserId = UserId });

        var act = async () => await db.SaveChangesAsync(CancellationToken);

        (await act.Should().ThrowAsync<DbUpdateException>(
                "user_planner_settings.user_id must be unique — the get-or-create in " +
                "GetPlannerSettingsEndpoint has no other backstop"))
            .WithInnerException<PostgresException>()
            .Which.SqlState.Should().Be(PostgresErrorCodes.UniqueViolation,
                "it is the unique index that rejects the second row, not a null or FK check");

        await using var verifyDb = CreateDbContext();
        (await verifyDb.Set<UserPlannerSettings>().AsNoTracking()
            .CountAsync(s => s.UserId == UserId, CancellationToken))
            .Should().Be(0, "the failed SaveChanges is one transaction — neither row survives");
    }

    /// <summary>
    /// The control for both of the above: with nobody else in the race the read still creates the row. Without
    /// it, "catch and carry on" could degrade into "never insert anything" and the two race tests would still
    /// pass off whichever caller did write.
    /// </summary>
    [Fact(DisplayName = "The uncontended first read still creates the row")]
    public async Task UncontendedFirstGet_StillCreatesTheRow()
    {
        (await CreateClient().GetAsync("api/planner/settings", CancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        await using var db = CreateDbContext();
        (await db.Set<UserPlannerSettings>().AsNoTracking()
            .CountAsync(s => s.UserId == UserId, CancellationToken)).Should().Be(1);
    }

    private static object ValidSettingsBody(int reminderMinutesBefore = 10) => new
    {
        RemindersEnabled = true,
        ReminderMinutesBefore = reminderMinutesBefore,
        AutoReminderMode = "ImportantOnly",
        AutoReminderMinImportance = 999,
        DetailsPanelExpandedByDefault = true,
        ArrowKeyNavEnabled = true,
        PredefinedSkipReasons = new[] { "Not needed today" },
        SlotDurationMinutes = 15,
        DefaultConflictResolution = "Ignore",
        DefaultApplyPreviewMode = true
    };
}
