using System.Net;
using System.Net.Http.Json;
using AdhdTimeOrganizer.Core.domain.model.entity.activity;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using AdhdTimeOrganizer.Routines.domain.model.entity.todoList;
using AdhdTimeOrganizer.Routines.infrastructure.jobs;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sydowwe.Framework.Contracts.scheduling;
using Sydowwe.Framework.Testing;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Routines;

/// <summary>
/// SEC-3 — the three sites that apply the lazy reset (the grouped read, the toggle, the nightly sweep) all
/// insert a <see cref="RoutinePeriodCompletion"/> keyed uniquely on <c>(TimePeriodId, PeriodStart)</c>, and
/// none of them locks. Two tabs in the same second, or a read landing while the 02:00 sweep is mid-flight,
/// both compute the same elapsed boundary and both write it.
/// <para>
/// The loser is simulated deterministically rather than raced: a completion row is seeded for exactly the
/// cycle the site is about to close, which is the one row the winner's transaction would have committed and
/// the only part of the reset that can collide. A real race also surfaces as a <c>row_version</c> conflict on
/// the period, which the same handler covers — see <c>RoutineResetConflict</c> for why both shapes mean the
/// same thing.
/// </para>
/// <para>
/// Un-fixed, every case here is a 500: the grouped read and the toggle base both call
/// <c>SaveChangesAsync</c> with no try/catch, and the sweep committed the whole night in one batch.
/// </para>
/// </summary>
[Collection("Postgres")]
public class RoutineResetConcurrencyTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    private const long UserId = FakeLoggedUserService.TestUserId;

    // ---- seeding ------------------------------------------------------------------------------------

    private static async Task<RoutineTimePeriod> SeedPeriodAsync(
        DbContext db, DateTime lastResetAt, int lengthInDays = 7, CancellationToken ct = default)
    {
        var period = new RoutineTimePeriod
        {
            UserId = UserId,
            // Unique per test: routine_time_period has unique indexes on (user_id, text) and (user_id, length_in_days).
            Text = $"Period {Guid.NewGuid():N}",
            Color = "#123456",
            LengthInDays = lengthInDays,
            ResetAnchorDay = 0,
            StreakThreshold = 100,
            StreakGraceDays = 0,
            LastResetAt = lastResetAt
        };
        db.Set<RoutineTimePeriod>().Add(period);
        await db.SaveChangesAsync(ct);
        return period;
    }

    private static async Task<RoutineTodoList> SeedItemAsync(
        DbContext db, RoutineTimePeriod period, bool isDone, CancellationToken ct)
    {
        var role = new ActivityRole { UserId = UserId, Name = $"Role {Guid.NewGuid():N}", Color = "#123456" };
        db.Set<ActivityRole>().Add(role);
        await db.SaveChangesAsync(ct);

        var activity = new Activity { UserId = UserId, Name = $"Activity {Guid.NewGuid():N}", RoleId = role.Id };
        db.Set<Activity>().Add(activity);
        await db.SaveChangesAsync(ct);

        var item = new RoutineTodoList
        {
            UserId = UserId,
            ActivityId = activity.Id,
            TimePeriodId = period.Id,
            IsDone = isDone
        };
        db.Set<RoutineTodoList>().Add(item);
        await db.SaveChangesAsync(ct);
        return item;
    }

    /// <summary>
    /// Writes the completion row the winner of the race would have written, for the cycle
    /// <paramref name="period"/> is about to close. Mirrors <c>RoutineResetService.TryReset</c>'s key
    /// derivation: the boundary is <c>LastResetAt + LengthInDays</c> truncated to midnight, and the row spans
    /// one period back from it.
    /// </summary>
    private static async Task SeedTheWinnersCompletionAsync(DbContext db, RoutineTimePeriod period, CancellationToken ct)
    {
        var periodEnd = DateOnly.FromDateTime(period.LastResetAt!.Value.AddDays(period.LengthInDays).Date);
        db.Set<RoutinePeriodCompletion>().Add(new RoutinePeriodCompletion
        {
            TimePeriodId = period.Id,
            PeriodStart = periodEnd.AddDays(-period.LengthInDays),
            PeriodEnd = periodEnd,
            CompletedCount = 1,
            TotalCount = 1
        });
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Runs the nightly handler the way the Scheduler's dispatcher would — one DI scope per fire.</summary>
    private async Task RunJobAsync()
    {
        await using var scope = Fixture.AdminAndUserFactory.Services.CreateAsyncScope();
        var handler = ActivatorUtilities.CreateInstance<RoutineTodoListResetJobHandler>(scope.ServiceProvider);

        var now = DateTime.UtcNow;
        await handler.ExecuteAsync(new ScheduledJobContext
        {
            ScheduledFireTimeUtc = now,
            ActualFireTimeUtc = now,
            JobKey = handler.Key,
            CorrelationId = "routine-reset-concurrency-test",
            TriggerSource = TriggerSource.Manual
        }, CancellationToken);
    }

    // ---- the grouped read ---------------------------------------------------------------------------

    /// <summary>
    /// The module's most-hit screen. A second tab that got there first must cost this request nothing worse
    /// than a wasted computation — and must not leave a second completion row behind.
    /// </summary>
    [Fact]
    public async Task GroupedRead_WhenAnotherCallerAlreadyClosedTheCycle_Serves200_AndWritesNoSecondRow()
    {
        RoutineTimePeriod period;
        await using (var db = CreateDbContext())
        {
            period = await SeedPeriodAsync(db, DateTime.UtcNow.AddDays(-8), ct: CancellationToken);
            await SeedItemAsync(db, period, isDone: true, CancellationToken);
            await SeedTheWinnersCompletionAsync(db, period, CancellationToken);
        }

        var response = await CreateClient().GetAsync(
            "api/routine-todo-list/grouped-by-time-period", CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK, "the loser of the race must not 500 the whole screen");

        await using var verify = CreateDbContext();
        var completions = await verify.Set<RoutinePeriodCompletion>().AsNoTracking()
            .Where(c => c.TimePeriodId == period.Id)
            .ToListAsync(CancellationToken);
        completions.Should().ContainSingle("the cycle is closed once, by whoever got there first");
    }

    /// <summary>
    /// The control: with nobody else in the race the read still applies the reset. Without this, "catch and
    /// carry on" could degrade into "never write anything" and the test above would still pass.
    /// </summary>
    [Fact]
    public async Task GroupedRead_WithNoCollision_StillClosesTheCycle()
    {
        RoutineTimePeriod period;
        RoutineTodoList item;
        await using (var db = CreateDbContext())
        {
            period = await SeedPeriodAsync(db, DateTime.UtcNow.AddDays(-8), ct: CancellationToken);
            item = await SeedItemAsync(db, period, isDone: true, CancellationToken);
        }

        var response = await CreateClient().GetAsync(
            "api/routine-todo-list/grouped-by-time-period", CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        await using var verify = CreateDbContext();
        (await verify.Set<RoutinePeriodCompletion>().AsNoTracking()
            .CountAsync(c => c.TimePeriodId == period.Id, CancellationToken)).Should().Be(1);
        (await verify.Set<RoutineTodoList>().AsNoTracking()
            .FirstAsync(i => i.Id == item.Id, CancellationToken)).IsDone.Should().BeFalse();
    }

    // ---- the toggle ---------------------------------------------------------------------------------

    /// <summary>
    /// The toggle applies the same lazy reset in <c>FetchAndPrepare</c>, and its base has no try/catch either.
    /// Losing the reset race must not cost the user their tick — which is why the reset is committed ahead of
    /// the toggle and the items re-read after a collision, rather than the exception simply being swallowed.
    /// </summary>
    [Fact]
    public async Task ToggleIsDone_WhenAnotherCallerAlreadyClosedTheCycle_StillTogglesTheItem()
    {
        RoutineTimePeriod period;
        RoutineTodoList item;
        await using (var db = CreateDbContext())
        {
            period = await SeedPeriodAsync(db, DateTime.UtcNow.AddDays(-8), ct: CancellationToken);
            item = await SeedItemAsync(db, period, isDone: false, CancellationToken);
            await SeedTheWinnersCompletionAsync(db, period, CancellationToken);
        }

        var response = await CreateClient().PatchAsJsonAsync(
            "api/routine-todo-list/toggle-is-done", new { Ids = new[] { item.Id } }, JsonOpts, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        await using var verify = CreateDbContext();
        var stored = await verify.Set<RoutineTodoList>().AsNoTracking()
            .FirstAsync(i => i.Id == item.Id, CancellationToken);
        stored.IsDone.Should().BeTrue("the toggle is the user's actual request; only the reset lost the race");

        (await verify.Set<RoutinePeriodCompletion>().AsNoTracking()
            .CountAsync(c => c.TimePeriodId == period.Id, CancellationToken)).Should().Be(1);
    }

    // ---- the nightly sweep --------------------------------------------------------------------------

    /// <summary>
    /// The finding's worst consequence. The sweep used to commit every period in one <c>SaveChangesAsync</c>,
    /// so a single collision with a request that had closed one period's cycle aborted the entire remaining
    /// night — no other period reset, and the grace mutations already accumulated in the same change tracker
    /// went with it. Asserted on the <i>other</i> period's rows, which is the part that used to be lost.
    /// </summary>
    [Fact]
    public async Task ResetSweep_WhenOnePeriodWasAlreadyClosed_StillResetsEveryOtherPeriod()
    {
        RoutineTimePeriod collided, untouched;
        RoutineTodoList collidedItem, untouchedItem;
        var lastResetAt = DateTime.UtcNow.AddDays(-8);
        await using (var db = CreateDbContext())
        {
            collided = await SeedPeriodAsync(db, lastResetAt, ct: CancellationToken);
            collidedItem = await SeedItemAsync(db, collided, isDone: true, CancellationToken);
            await SeedTheWinnersCompletionAsync(db, collided, CancellationToken);

            // A different LengthInDays: routine_time_period has a unique index on (user_id, length_in_days).
            untouched = await SeedPeriodAsync(db, lastResetAt, lengthInDays: 5, ct: CancellationToken);
            untouchedItem = await SeedItemAsync(db, untouched, isDone: true, CancellationToken);
        }

        await RunJobAsync();

        await using var verify = CreateDbContext();

        var swept = await verify.Set<RoutineTimePeriod>().AsNoTracking()
            .FirstAsync(p => p.Id == untouched.Id, CancellationToken);
        swept.LastResetAt.Should().NotBe(lastResetAt, "the collision on another period must not abort the sweep");
        swept.Streak.Should().Be(1);

        (await verify.Set<RoutineTodoList>().AsNoTracking()
            .FirstAsync(i => i.Id == untouchedItem.Id, CancellationToken)).IsDone.Should().BeFalse();
        (await verify.Set<RoutinePeriodCompletion>().AsNoTracking()
            .CountAsync(c => c.TimePeriodId == untouched.Id, CancellationToken)).Should().Be(1);

        // And the period that lost the race is left exactly as the winner left it — one row, nothing doubled,
        // and its own state rolled back rather than half-applied.
        (await verify.Set<RoutinePeriodCompletion>().AsNoTracking()
            .CountAsync(c => c.TimePeriodId == collided.Id, CancellationToken)).Should().Be(1);
        (await verify.Set<RoutineTodoList>().AsNoTracking()
            .FirstAsync(i => i.Id == collidedItem.Id, CancellationToken)).IsDone.Should().BeTrue();
    }

    // ---- SEC-5: the first-time settings write -------------------------------------------------------

    /// <summary>
    /// SEC-5 — <c>PUT /routine/settings</c> reads, finds nothing, and inserts against the unique
    /// <c>user_id</c> index. Two concurrent first writes both miss the read and one used to get an unhandled
    /// <c>DbUpdateException</c>.
    /// <para>
    /// Raced rather than simulated, because unlike the reset there is no way to reach the recovery branch
    /// deterministically: it requires the row to be absent at read time and present at insert time. Two
    /// writers is the scenario the finding describes — one dismissal click arriving twice — and is also the
    /// shape the recovery is written for: the single loser re-reads the winner's row and updates it, with
    /// nobody left to contend with. In the rare run where the two do not overlap the test passes vacuously
    /// rather than flaking.
    /// </para>
    /// </summary>
    [Fact]
    public async Task FirstTimeSettingsWrite_UnderConcurrency_AllSucceed_AndLeaveOneRow()
    {
        var writes = Enumerable.Range(0, 2)
            .Select(i => CreateClient().PutAsJsonAsync(
                "api/routine/settings",
                new { RoutineReviewDismissedForWeekStart = new DateOnly(2026, 8, 17).AddDays(i * 7).ToString("yyyy-MM-dd") },
                JsonOpts,
                CancellationToken))
            .ToList();

        var responses = await Task.WhenAll(writes);

        responses.Should().OnlyContain(r => r.StatusCode == HttpStatusCode.NoContent,
            "losing the insert race is not the caller's problem — the loser updates the winning row");

        await using var db = CreateDbContext();
        (await db.Set<UserRoutineSettings>().AsNoTracking()
            .CountAsync(s => s.UserId == UserId, CancellationToken))
            .Should().Be(1, "the unique index holds and nobody inserted a second row");
    }

    /// <summary>
    /// SEC-5's residual — the <i>other</i> half of the same endpoint, and the commoner one. Once the row
    /// exists, concurrent writes no longer collide on the unique index at all: they lose on the
    /// <c>row_version</c> token every table through <c>BaseEntityConfigure</c> carries, which is a
    /// <c>DbUpdateConcurrencyException</c> and was not caught by the unique-violation handler the first-write
    /// fix added. The loser got a 500 — indistinguishable, to a client, from the server being broken.
    /// <para>
    /// Now on <c>BaseUserSettingsUpsertEndpoint</c>, whose outer handler runs <c>DbUtils.HandleException</c>,
    /// so the loser is told it lost: 409. The assertion is therefore "no 5xx", not "some 409" — five writers
    /// that happen to serialise are five 204s and equally correct, and demanding a conflict would make the
    /// test flake on a fast box. What it rules out is the one status that cannot be right.
    /// </para>
    /// <para>
    /// Five writers rather than two: the losers of the first round re-read and contend again, which is what
    /// surfaced this residual in the first place.
    /// </para>
    /// </summary>
    [Fact]
    public async Task ExistingSettingsRow_UnderConcurrency_NeverAnswers500()
    {
        var seed = await CreateClient().PutAsJsonAsync(
            "api/routine/settings",
            new { RoutineReviewDismissedForWeekStart = "2026-08-10" },
            JsonOpts,
            CancellationToken);
        seed.StatusCode.Should().Be(HttpStatusCode.NoContent, "the row has to exist before the update race means anything");

        var weeks = Enumerable.Range(0, 5).Select(i => new DateOnly(2026, 8, 17).AddDays(i * 7)).ToList();
        var responses = await Task.WhenAll(weeks.Select(w => CreateClient().PutAsJsonAsync(
            "api/routine/settings",
            new { RoutineReviewDismissedForWeekStart = w.ToString("yyyy-MM-dd") },
            JsonOpts,
            CancellationToken)));

        responses.Should().OnlyContain(r => r.StatusCode == HttpStatusCode.NoContent || r.StatusCode == HttpStatusCode.Conflict,
            "a lost row_version race is a 409 the client can retry, never a 500");

        await using var db = CreateDbContext();
        var settings = await db.Set<UserRoutineSettings>().AsNoTracking()
            .SingleAsync(s => s.UserId == UserId, CancellationToken);

        // Whoever won, the row holds a value somebody actually sent — a recovery branch that re-applied a
        // stale request would leave the seed value behind instead.
        settings.RoutineReviewDismissedForWeekStart.Should().BeOneOf(weeks.Cast<DateOnly?>(),
            "one of the five writers won outright; none of them asked for the seed value");
    }
}
