using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using AdhdTimeOrganizer.Routines.domain.model.entity.todoList;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.Testing;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Endpoints;

/// <summary>
/// TEST-11 — <c>PUT /api/routine-time-period/{id}</c>. The create path is covered by
/// <see cref="RoutineTimePeriodUniqueIndexTests"/> and <see cref="RoutineTimePeriodRangeConstraintTests"/>;
/// the update path carries three rules that live only in
/// <c>RoutineTimePeriodRequest.UpdateEntity</c> and were asserted nowhere:
/// the freeze-budget clamp, the schedule-change reset of the nudge idempotency mark, and the
/// deliberate omission of the streak fields from the writable set.
///
/// <para>IDOR is covered by <c>UpdateRoutineTimePeriodEndpointTests</c> in
/// <c>TodoListCrudAuthMatrixTests.cs</c> and is not repeated here.</para>
/// </summary>
[Collection("Postgres")]
public class RoutineTimePeriodUpdateTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    private const long UserId = FakeLoggedUserService.TestUserId;

    /// <summary>The mark the nudge sweep keys on — any instant will do, it just has to survive or not.</summary>
    private static readonly DateTime NotifiedFor = new(2026, 3, 1, 9, 0, 0, DateTimeKind.Utc);

    private static readonly DateTime LastReset = new(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>
    /// Seeds a period with the fields <c>TodoListTestSeedHelper.SeedRoutineTimePeriodAsync</c> does not expose —
    /// the freeze counters, the streak state and the nudge mark are exactly what this suite is about.
    /// </summary>
    private static async Task<long> SeedPeriodAsync(
        DbContext db, string text, int lengthInDays, int resetAnchorDay = 1,
        int freezeBudget = 2, int? freezesRemaining = null, int streakGraceDays = 0, int historyDepth = 16,
        int? reminderLeadDays = null, DateTime? endingSoonNotifiedFor = null,
        int streak = 0, int bestStreak = 0, DateTime? lastResetAt = null, DateTime? graceNotifiedFor = null,
        long userId = UserId)
    {
        var entity = new RoutineTimePeriod
        {
            UserId = userId,
            Text = text,
            Color = "#abcdef",
            LengthInDays = lengthInDays,
            ResetAnchorDay = resetAnchorDay,
            StreakThreshold = 90,
            StreakGraceDays = streakGraceDays,
            HistoryDepth = historyDepth,
            ReminderLeadDays = reminderLeadDays,
            FreezeBudget = freezeBudget,
            FreezesRemaining = freezesRemaining ?? freezeBudget,
            EndingSoonNotifiedFor = endingSoonNotifiedFor,
            Streak = streak,
            BestStreak = bestStreak,
            LastResetAt = lastResetAt,
            GraceNotifiedFor = graceNotifiedFor
        };
        db.Set<RoutineTimePeriod>().Add(entity);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return entity.Id;
    }

    /// <summary>
    /// A full valid body. The endpoint is a full update, so every field has to be present on every PUT —
    /// omitting one silently writes the request record's default rather than leaving the stored value alone.
    /// </summary>
    private static object Payload(string text, int lengthInDays, int resetAnchorDay = 1, string color = "#abcdef",
        int freezeBudget = 2, int streakGraceDays = 0, int historyDepth = 16, int? reminderLeadDays = null) => new
    {
        Text = text,
        Color = color,
        LengthInDays = lengthInDays,
        IsHidden = false,
        ResetAnchorDay = resetAnchorDay,
        StreakThreshold = 90,
        StreakGraceDays = streakGraceDays,
        HistoryDepth = historyDepth,
        ReminderLeadDays = reminderLeadDays,
        FreezeBudget = freezeBudget
    };

    private Task<HttpResponseMessage> UpdateAsync(long id, object payload) =>
        CreateClient().PutAsJsonAsync($"api/routine-time-period/{id}", payload, JsonOpts, CancellationToken);

    private async Task<RoutineTimePeriod> LoadAsync(long id)
    {
        await using var db = CreateDbContext();
        return await db.Set<RoutineTimePeriod>().SingleAsync(p => p.Id == id, CancellationToken);
    }

    private async Task ShouldSucceed(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync(CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);
    }

    /// <summary>
    /// Same shape as <c>RoutineTimePeriodRangeConstraintTests.ShouldBeValidationErrorFor</c>: the field key is
    /// the only observable difference between the validator rejecting the value and the database's matching
    /// CHECK constraint doing it after a wasted round trip.
    /// </summary>
    private async Task ShouldBeValidationErrorFor(HttpResponseMessage response, string property)
    {
        var body = await response.Content.ReadAsStringAsync(CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest, body);

        var root = JsonDocument.Parse(body).RootElement;
        root.TryGetProperty("errors", out var errors).Should().BeTrue(body);

        errors.EnumerateObject().Select(e => e.Name)
            .Should().Contain(name => string.Equals(name, property, StringComparison.OrdinalIgnoreCase),
                $"a validator rule keys the failure to {property}; a DB check violation reaches the client without it");
    }

    // ---------------------------------------------------------------------------------------------------
    // Rule 1 — lowering the budget clamps the remaining count
    // ---------------------------------------------------------------------------------------------------

    /// <summary>
    /// <c>RoutineTimePeriodRequest.UpdateEntity</c>'s <c>Math.Min</c>. Without it
    /// <c>ck_routine_time_period_freezes_remaining_range</c> (<c>freezes_remaining &lt;= freeze_budget</c>)
    /// turns an ordinary settings edit into a 400.
    /// </summary>
    [Fact(DisplayName = "Lowering FreezeBudget clamps FreezesRemaining down to it")]
    public async Task Update_LoweringTheFreezeBudget_ClampsFreezesRemaining()
    {
        long id;
        await using (var db = CreateDbContext())
            id = await SeedPeriodAsync(db, "clamp-down", lengthInDays: 7, freezeBudget: 5, freezesRemaining: 5);

        var response = await UpdateAsync(id, Payload("clamp-down", lengthInDays: 7, freezeBudget: 2));

        await ShouldSucceed(response);
        var stored = await LoadAsync(id);
        stored.FreezeBudget.Should().Be(2);
        stored.FreezesRemaining.Should().Be(2, "the remaining count may never exceed the budget");
    }

    /// <summary>
    /// The other half, and the reason the code uses <c>Math.Min</c> rather than an assignment: a budget that is
    /// lowered to a value still above the spent-down remainder must not refill it. Editing a setting cannot hand
    /// out freezes.
    /// </summary>
    [Fact(DisplayName = "Lowering FreezeBudget to above the spent remainder does not top it up")]
    public async Task Update_LoweringTheFreezeBudget_DoesNotTopUpASpentBudget()
    {
        long id;
        await using (var db = CreateDbContext())
            id = await SeedPeriodAsync(db, "clamp-spent", lengthInDays: 7, freezeBudget: 5, freezesRemaining: 1);

        var response = await UpdateAsync(id, Payload("clamp-spent", lengthInDays: 7, freezeBudget: 3));

        await ShouldSucceed(response);
        var stored = await LoadAsync(id);
        stored.FreezeBudget.Should().Be(3);
        stored.FreezesRemaining.Should().Be(1, "a spent freeze stays spent — only the refill window returns it");
    }

    [Fact(DisplayName = "Raising FreezeBudget leaves FreezesRemaining alone")]
    public async Task Update_RaisingTheFreezeBudget_LeavesRemainingAlone()
    {
        long id;
        await using (var db = CreateDbContext())
            id = await SeedPeriodAsync(db, "raise-budget", lengthInDays: 7, freezeBudget: 2, freezesRemaining: 1);

        var response = await UpdateAsync(id, Payload("raise-budget", lengthInDays: 7, freezeBudget: 4));

        await ShouldSucceed(response);
        var stored = await LoadAsync(id);
        stored.FreezeBudget.Should().Be(4);
        stored.FreezesRemaining.Should().Be(1, "the refill is RoutineStreakFreezeService's job, on its own window");
    }

    // ---------------------------------------------------------------------------------------------------
    // Rule 2 — a schedule change clears the nudge idempotency mark
    // ---------------------------------------------------------------------------------------------------

    /// <summary>
    /// <c>LengthInDays</c> is an input to <c>RoutineResetService.ComputeNextReset</c>: moving it moves the reset
    /// instant, so the mark keyed to the old one is stale and the new window has to earn a fresh nudge.
    /// </summary>
    [Fact(DisplayName = "Changing LengthInDays clears EndingSoonNotifiedFor")]
    public async Task Update_ChangingLengthInDays_ClearsTheEndingSoonMark()
    {
        long id;
        await using (var db = CreateDbContext())
            id = await SeedPeriodAsync(db, "sched-length", lengthInDays: 7, reminderLeadDays: 2,
                endingSoonNotifiedFor: NotifiedFor);

        // 10 is not weekly-aligned, so anchor 1 is read as a day of month — still inside BETWEEN 0 AND 30.
        var response = await UpdateAsync(id, Payload("sched-length", lengthInDays: 10, reminderLeadDays: 2));

        await ShouldSucceed(response);
        (await LoadAsync(id)).EndingSoonNotifiedFor.Should().BeNull();
    }

    [Fact(DisplayName = "Changing ResetAnchorDay clears EndingSoonNotifiedFor")]
    public async Task Update_ChangingResetAnchorDay_ClearsTheEndingSoonMark()
    {
        long id;
        await using (var db = CreateDbContext())
            id = await SeedPeriodAsync(db, "sched-anchor", lengthInDays: 7, resetAnchorDay: 1, reminderLeadDays: 2,
                endingSoonNotifiedFor: NotifiedFor);

        var response = await UpdateAsync(id,
            Payload("sched-anchor", lengthInDays: 7, resetAnchorDay: 3, reminderLeadDays: 2));

        await ShouldSucceed(response);
        (await LoadAsync(id)).EndingSoonNotifiedFor.Should().BeNull();
    }

    /// <summary>
    /// The control for the two above, and the one that catches someone "simplifying" the <c>scheduleChanged</c>
    /// guard into an unconditional clear: a colour change mid-window leaves the reset instant where it was, so
    /// clearing the mark would re-nudge the user about a period they have already been nudged about.
    /// </summary>
    [Fact(DisplayName = "Changing only the colour keeps EndingSoonNotifiedFor")]
    public async Task Update_ChangingOnlyTheColour_KeepsTheEndingSoonMark()
    {
        long id;
        await using (var db = CreateDbContext())
            id = await SeedPeriodAsync(db, "sched-colour", lengthInDays: 7, resetAnchorDay: 1, reminderLeadDays: 2,
                endingSoonNotifiedFor: NotifiedFor);

        var response = await UpdateAsync(id,
            Payload("sched-colour", lengthInDays: 7, resetAnchorDay: 1, color: "#123456", reminderLeadDays: 2));

        await ShouldSucceed(response);
        var stored = await LoadAsync(id);
        stored.Color.Should().Be("#123456", "the edit itself must still land");
        stored.EndingSoonNotifiedFor.Should().Be(NotifiedFor,
            "the schedule did not move, so the nudge already sent for this window must stay suppressed");
    }

    // ---------------------------------------------------------------------------------------------------
    // Rule 3 — streak fields are not writable from a request
    // ---------------------------------------------------------------------------------------------------

    /// <summary>
    /// Mass-assignment guard. <c>UpdateEntity</c> deliberately omits <c>Streak</c>, <c>BestStreak</c>,
    /// <c>LastResetAt</c>, <c>StreakGraceUntil</c> and <c>GraceNotifiedFor</c>, and the request record has no
    /// such properties — so a body carrying them is ignored by the binder rather than granting a streak or
    /// silencing a grace warning. Adding those properties to the DTO "for symmetry" is exactly what this fails on.
    /// </summary>
    [Fact(DisplayName = "A body carrying streak fields cannot write them")]
    public async Task Update_CannotSetTheStreakThroughTheRequest()
    {
        long id;
        await using (var db = CreateDbContext())
            // FreezesRemaining is seeded *below* the budget on purpose: had it been seeded at the budget, the
            // clamp in UpdateEntity would floor an injected 99 back to 2 and the assertion below could not fail.
            id = await SeedPeriodAsync(db, "mass-assign", lengthInDays: 7, freezeBudget: 2, freezesRemaining: 1,
                streak: 3, bestStreak: 5, lastResetAt: LastReset, graceNotifiedFor: NotifiedFor);

        var response = await UpdateAsync(id, new
        {
            Text = "mass-assign",
            Color = "#abcdef",
            LengthInDays = 7,
            IsHidden = false,
            ResetAnchorDay = 1,
            StreakThreshold = 90,
            StreakGraceDays = 0,
            HistoryDepth = 16,
            FreezeBudget = 2,
            // Not on the request record — the attack is that the binder would take them anyway.
            Streak = 999,
            BestStreak = 999,
            LastResetAt = "2020-01-01T00:00:00Z",
            StreakGraceUntil = "2020-01-01T00:00:00Z",
            GraceNotifiedFor = (string?)null, // clearing this one would silence a grace warning
            FreezesRemaining = 99
        });

        await ShouldSucceed(response);
        var stored = await LoadAsync(id);
        stored.Streak.Should().Be(3);
        stored.BestStreak.Should().Be(5);
        stored.LastResetAt.Should().Be(LastReset);
        stored.StreakGraceUntil.Should().BeNull();
        stored.GraceNotifiedFor.Should().Be(NotifiedFor, "a request must not be able to un-suppress or silence a grace warning");
        stored.FreezesRemaining.Should().Be(1, "the remaining count is spent and refilled by the service, never set");
    }

    // ---------------------------------------------------------------------------------------------------
    // The two unique indexes, reached through update rather than create
    // ---------------------------------------------------------------------------------------------------

    [Fact(DisplayName = "Renaming a period onto another period's Text 409s and leaves the row alone")]
    public async Task Update_ToADuplicateText_Returns409()
    {
        long id;
        await using (var db = CreateDbContext())
        {
            await SeedPeriodAsync(db, "taken-name", lengthInDays: 14);
            id = await SeedPeriodAsync(db, "my-name", lengthInDays: 7);
        }

        var response = await UpdateAsync(id, Payload("taken-name", lengthInDays: 7));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict,
            await response.Content.ReadAsStringAsync(CancellationToken));
        (await LoadAsync(id)).Text.Should().Be("my-name");
    }

    /// <summary>
    /// <c>(UserId, LengthInDays)</c> is the index people forget, and update is the easier way to hit it by
    /// accident: two periods that were created with distinct lengths collide the moment one is edited onto the
    /// other's length, with no text clash to warn anyone.
    /// </summary>
    [Fact(DisplayName = "Editing a period onto another period's LengthInDays ALSO 409s")]
    public async Task Update_ToADuplicateLengthInDays_Returns409()
    {
        long id;
        await using (var db = CreateDbContext())
        {
            await SeedPeriodAsync(db, "fortnightly", lengthInDays: 14);
            id = await SeedPeriodAsync(db, "weekly", lengthInDays: 7);
        }

        var response = await UpdateAsync(id, Payload("weekly", lengthInDays: 14));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict,
            await response.Content.ReadAsStringAsync(CancellationToken));
        (await LoadAsync(id)).LengthInDays.Should().Be(7);
    }

    // ---------------------------------------------------------------------------------------------------
    // Validator rules evaluated against the new values
    // ---------------------------------------------------------------------------------------------------

    /// <summary>
    /// <c>RoutineTimePeriodValidator</c>'s cross-field rule (<c>StreakGraceDays &lt; LengthInDays</c>) is
    /// evaluated against the <i>new</i> length, so shrinking a period below a grace it already carries is a
    /// field-keyed 400 rather than the DB's matching CHECK constraint.
    /// </summary>
    [Fact(DisplayName = "Lowering LengthInDays below the existing StreakGraceDays is a 400 naming the field")]
    public async Task Update_LoweringLengthInDaysBelowStreakGraceDays_Returns400()
    {
        long id;
        await using (var db = CreateDbContext())
            id = await SeedPeriodAsync(db, "shrink-below-grace", lengthInDays: 7, streakGraceDays: 3);

        var response = await UpdateAsync(id,
            Payload("shrink-below-grace", lengthInDays: 3, resetAnchorDay: 1, streakGraceDays: 3));

        await ShouldBeValidationErrorFor(response, "streakGraceDays");
        (await LoadAsync(id)).LengthInDays.Should().Be(7, "a rejected update must leave the row untouched");
    }

    /// <summary>
    /// CQ-3's two rules mirror <c>ck_routine_time_period_history_depth_range</c> and
    /// <c>…_length_in_days_range</c> in the validator. They live on the shared request record, so they must key
    /// the failure to the field on the update path exactly as
    /// <see cref="RoutineTimePeriodRangeConstraintTests"/> pins them on create.
    /// </summary>
    [Theory(DisplayName = "The CQ-3 ranges are enforced on update too, naming the field")]
    [InlineData(7, 0, "historyDepth")]
    [InlineData(7, 101, "historyDepth")]
    [InlineData(366, 16, "lengthInDays")]
    public async Task Update_OutOfRange_Returns400NamingTheField(int lengthInDays, int historyDepth, string property)
    {
        long id;
        await using (var db = CreateDbContext())
            id = await SeedPeriodAsync(db, "range-on-update", lengthInDays: 5);

        var response = await UpdateAsync(id, Payload("range-on-update",
            lengthInDays: lengthInDays, resetAnchorDay: 0, historyDepth: historyDepth));

        await ShouldBeValidationErrorFor(response, property);
        var stored = await LoadAsync(id);
        stored.LengthInDays.Should().Be(5);
        stored.HistoryDepth.Should().Be(16);
    }
}
