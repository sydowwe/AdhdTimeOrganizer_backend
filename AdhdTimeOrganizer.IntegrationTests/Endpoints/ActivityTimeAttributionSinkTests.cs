using AdhdTimeOrganizer.Core.application.seam;
using AdhdTimeOrganizer.Core.domain.model.entity.activity;
using AdhdTimeOrganizer.Core.domain.model.entity.user;
using AdhdTimeOrganizer.History.domain.model.entity.activityHistory;
using AdhdTimeOrganizer.History.domain.model.@enum;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sydowwe.Framework.domain.@enum;
using Sydowwe.Framework.domain.valueObject;
using Sydowwe.Framework.Testing;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Endpoints;

/// <summary>
/// TEST-4 — <c>ActivityHistoryTimeAttributionSink</c>, History's implementation of Core's
/// <see cref="IActivityTimeAttributionSink"/>. It had no tests at all while sitting on the desktop
/// heartbeat's write path (<c>DesktopActivityHeartbeatEndpoint</c> calls both of its methods);
/// <c>SeamWiringTests</c> proved the seam <i>resolves</i>, nothing proved it <i>computes</i>.
///
/// <para>The sink is resolved out of DI rather than newed up, so these also cover its registration.
/// There is no HTTP route behind it — it is a seam — so these are service-level tests over a real
/// <c>DbContext</c>.</para>
///
/// <para><b>Two rules from <c>docs/summary.md</c> are load-bearing and each has a test below.</b>
/// The sink mutates the context handed to it and must never call <c>SaveChanges</c> — the caller's own
/// save is the transaction that keeps a tracked entry atomic with its attribution. And a window whose
/// start equals an existing row's <c>EndTimestamp</c> <i>extends</i> that row rather than inserting a
/// second one, which is a property of the ledger's shape and the reason the logic lives in History.</para>
///
/// <para><b>On <c>CQ-5</c>.</b> The finding is fixed (commit <c>e624797</c>): the day is now bounded
/// half-open on UTC-kinded midnights. The two tests that would have caught it —
/// <see cref="TotalSecondsBetweenAsync_SumsOnlyThatActivityOnThatUtcDay"/> and
/// <see cref="TotalSecondsBetweenAsync_IsHalfOpenOnTheUpperBound"/> — are kept as regressions, and they
/// are load-bearing again: the harness used to enable <c>Npgsql.EnableLegacyTimestampBehavior</c>, which
/// made every kind bug invisible to a round trip (<c>INFRA-1</c>), and the submodule has since dropped
/// that switch. A round trip here now fails on an <c>Unspecified</c> bound exactly as production would.</para>
///
/// <para><b>Three new findings are pinned below with <c>[Trait("Status","KnownGap")]</c></b>, all from
/// <see cref="IntTime"/> storing only hours and minutes — see
/// <see cref="AttributeAsync_WithSubMinuteSeconds_RecordsNothing"/> for the one that matters.</para>
/// </summary>
[Collection("Postgres")]
public class ActivityTimeAttributionSinkTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    private const long UserId = FakeLoggedUserService.TestUserId;

    /// <summary>Pinned rather than derived from <c>UtcNow</c>: a run crossing UTC midnight would otherwise
    /// seed one day and query another (a flakiness risk `01-testing.md` calls out for this slice).</summary>
    private static readonly DateOnly Day = new(2026, 3, 1);

    /// <summary>The UTC day the totals below are read over. The sink takes instants since bugs-to-investigate
    /// #1 — whose day it is belongs to the caller — so a UTC day is just one range among many.</summary>
    private static readonly DateTime DayStart = Day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

    private static readonly DateTime DayEnd = DayStart.AddDays(1);

    private static readonly DateTime TenAm = new(2026, 3, 1, 10, 0, 0, DateTimeKind.Utc);

    private readonly IServiceScope _scope = fixture.UnauthenticatedFactory.Services.CreateScope();

    private IActivityTimeAttributionSink Sink => _scope.ServiceProvider.GetRequiredService<IActivityTimeAttributionSink>();

    public override ValueTask DisposeAsync()
    {
        _scope.Dispose();
        return base.DisposeAsync();
    }

    // ---- the no-SaveChanges contract -------------------------------------------------------------

    /// <summary>
    /// With nothing contiguous, one row is <b>added to the change tracker and left unsaved</b>.
    ///
    /// <para>The unsaved half is the point. Core's interface says an implementation that saves "splits one
    /// commit into two and can leave tracked entries with no attribution if the second one fails", and the
    /// heartbeat relies on it: it writes raw entries and their attribution under a single
    /// <c>TrySaveIngestBatchAsync</c>. Nothing but this test would notice someone "helpfully" adding a save,
    /// so the assertion is made against a <i>second</i> context — the tracker alone cannot tell an unsaved
    /// entity from a saved one.</para>
    /// </summary>
    [Fact]
    public async Task AttributeAsync_AddsOneUnsavedRow_AndDoesNotSaveItItself()
    {
        await using var db = CreateDbContext();
        var activityId = await SeedActivityAsync(db, UserId);

        await Sink.AttributeAsync(db, UserId, activityId, TenAm, 300, CancellationToken);

        db.ChangeTracker.Entries<ActivityHistory>()
            .Count(e => e.State == EntityState.Added)
            .Should().Be(1, "one non-contiguous window is one new ledger row");

        await using (var other = CreateDbContext())
        {
            (await other.Set<ActivityHistory>().CountAsync(h => h.ActivityId == activityId, CancellationToken))
                .Should().Be(0, "the sink must leave the row unsaved -- the caller's save is the transaction " +
                               "that keeps the attribution atomic with the tracked entries it came from");
        }

        await db.SaveChangesAsync(CancellationToken);

        var row = await SingleRowAsync(activityId);
        row.StartTimestamp.Should().Be(TenAm);
        row.EndTimestamp.Should().Be(TenAm.AddSeconds(300));
        row.Length.TotalSeconds.Should().Be(300);
    }

    // ---- the contiguous-window extension rule ----------------------------------------------------

    /// <summary>
    /// A window starting exactly where an existing row ends extends that row — <c>EndTimestamp</c> moves and
    /// <c>Length</c> grows — instead of inserting a second one. This is the rule the whole class exists for:
    /// without it the ledger fragments into one row per heartbeat interval.
    /// </summary>
    [Fact]
    public async Task AttributeAsync_ExtendsAContiguousWindow_InsteadOfInserting()
    {
        await using var db = CreateDbContext();
        var activityId = await SeedActivityAsync(db, UserId);
        await SeedHistoryAsync(db, UserId, activityId, TenAm, 300);

        await Sink.AttributeAsync(db, UserId, activityId, TenAm.AddSeconds(300), 180, CancellationToken);
        await db.SaveChangesAsync(CancellationToken);

        var row = await SingleRowAsync(activityId);
        row.StartTimestamp.Should().Be(TenAm, "extending must not move the start");
        row.EndTimestamp.Should().Be(TenAm.AddSeconds(480));
        row.Length.TotalSeconds.Should().Be(480, "the two windows are one stretch of activity seen twice");
    }

    /// <summary>
    /// The rule is equality on the instant, not proximity: a one-minute gap after the previous end is a
    /// second row. A sink that extended on "close enough" would silently absorb the idle minute into the
    /// activity's total.
    /// </summary>
    [Fact]
    public async Task AttributeAsync_WithAGapAfterThePreviousEnd_InsertsASecondRow()
    {
        await using var db = CreateDbContext();
        var activityId = await SeedActivityAsync(db, UserId);
        await SeedHistoryAsync(db, UserId, activityId, TenAm, 300);

        await Sink.AttributeAsync(db, UserId, activityId, TenAm.AddSeconds(360), 180, CancellationToken);
        await db.SaveChangesAsync(CancellationToken);

        await using var read = CreateDbContext();
        var rows = await read.Set<ActivityHistory>()
            .Where(h => h.ActivityId == activityId)
            .OrderBy(h => h.StartTimestamp)
            .ToListAsync(CancellationToken);

        rows.Should().HaveCount(2, "a window that does not start exactly where the last one ended is a new stretch");
        rows[0].EndTimestamp.Should().Be(TenAm.AddSeconds(300), "the earlier row must be left alone");
        rows[1].StartTimestamp.Should().Be(TenAm.AddSeconds(360));
    }

    // ---- TotalSecondsBetweenAsync ------------------------------------------------------------------

    /// <summary>
    /// Sums one activity on one UTC day and nothing else — proving the day filter and the activity filter
    /// at once. Three rows in the day (300 + 180 + 60), one on the next day, one on the same day for a
    /// different activity; only 540 may come back.
    ///
    /// <para>This is also the round trip that settles <c>CQ-5</c> item 1. The bounds are built with
    /// <c>day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)</c>; without the kind Npgsql refuses the
    /// parameter against <c>start_timestamp</c> (a <c>timestamptz</c>) and this throws rather than
    /// returning a wrong number. It could not have caught that while the harness enabled the legacy
    /// timestamp switch — that switch is gone, so it can now.</para>
    /// </summary>
    [Fact]
    public async Task TotalSecondsBetweenAsync_SumsOnlyThatActivityOnThatUtcDay()
    {
        await using var db = CreateDbContext();
        var activityId = await SeedActivityAsync(db, UserId);
        var otherActivityId = await SeedActivityAsync(db, UserId);

        await SeedHistoryAsync(db, UserId, activityId, TenAm, 300);
        await SeedHistoryAsync(db, UserId, activityId, TenAm.AddHours(1), 180);
        await SeedHistoryAsync(db, UserId, activityId, TenAm.AddHours(2), 60);
        await SeedHistoryAsync(db, UserId, activityId, TenAm.AddDays(1), 900);
        await SeedHistoryAsync(db, UserId, otherActivityId, TenAm.AddMinutes(30), 1200);

        var total = await Sink.TotalSecondsBetweenAsync(db, UserId, activityId, DayStart, DayEnd, CancellationToken);

        total.Should().Be(540,
            "only the three rows for this activity on this UTC day count -- the next day's 900s and the " +
            "other activity's 1200s are both excluded, and each exclusion is a plausible wrong total");
    }

    /// <summary>
    /// The range is <b>half-open</b> — <c>[from, to)</c>: a record ending exactly on the upper bound is wholly
    /// inside, and one starting on it is wholly outside (<c>CQ-5</c> item 2; the predicate was once closed on
    /// <c>TimeOnly.MaxValue</c> and dropped whatever landed in the final tick).
    /// <para>
    /// This used to seed a record starting in the day's last microsecond and running five minutes past midnight,
    /// and expect all five minutes on the day. Since records count by overlap those minutes belong to the next
    /// day, so the boundary is now probed by a record that <i>ends</i> on it.
    /// </para>
    /// </summary>
    [Fact]
    public async Task TotalSecondsBetweenAsync_IsHalfOpenOnTheUpperBound()
    {
        await using var db = CreateDbContext();
        var activityId = await SeedActivityAsync(db, UserId);

        await SeedHistoryAsync(db, UserId, activityId, DayEnd.AddSeconds(-300), 300);
        await SeedHistoryAsync(db, UserId, activityId, DayEnd, 600);

        var total = await Sink.TotalSecondsBetweenAsync(db, UserId, activityId, DayStart, DayEnd, CancellationToken);

        total.Should().Be(300,
            "a record ending on the upper bound is inside the range, and one starting on it is not");
    }

    /// <summary>
    /// A record straddling a bound counts pro rata. <c>AttributeAsync</c> grows one record across contiguous
    /// heartbeats, so an hour and a half of unbroken work is a single 09:00–10:30 record; asked for "since
    /// 09:30" — which is what the completion automation asks once an earlier same-activity block is done — the
    /// answer is the hour after 09:30, not zero (a start-only predicate) and not the whole 90 minutes.
    /// </summary>
    [Fact]
    public async Task TotalSecondsBetweenAsync_CountsOnlyThePartOfAStraddlingRecordInsideTheRange()
    {
        await using var db = CreateDbContext();
        var activityId = await SeedActivityAsync(db, UserId);
        var nineAm = DayStart.AddHours(9);

        await SeedHistoryAsync(db, UserId, activityId, nineAm, 90 * 60);

        var total = await Sink.TotalSecondsBetweenAsync(db, UserId, activityId, nineAm.AddMinutes(30), DayEnd, CancellationToken);

        total.Should().Be(60 * 60);
    }

    // ---- edges on AttributeAsync -----------------------------------------------------------------

    /// <summary>
    /// Zero seconds writes a zero-length row rather than nothing. That is legitimate and deliberate — a
    /// zero-length recording is a shape this codebase supports elsewhere — so it is pinned as behaviour
    /// rather than flagged. Note the heartbeat cannot reach it anyway:
    /// <c>DesktopActivityHeartbeatValidator</c> requires one of the two second counts to be positive and
    /// <c>CollapseCollidingEntries</c> drops all-zero entries.
    /// </summary>
    [Fact]
    public async Task AttributeAsync_WithZeroSeconds_WritesAZeroLengthRow()
    {
        await using var db = CreateDbContext();
        var activityId = await SeedActivityAsync(db, UserId);

        await Sink.AttributeAsync(db, UserId, activityId, TenAm, 0, CancellationToken);
        await db.SaveChangesAsync(CancellationToken);

        var row = await SingleRowAsync(activityId);
        row.StartTimestamp.Should().Be(TenAm);
        row.EndTimestamp.Should().Be(TenAm, "a zero-second window is an instant, not an inverted interval");
        row.Length.TotalSeconds.Should().Be(0);
    }

    /// <summary>
    /// <b>New finding, pinned not fixed.</b> <c>AttributeAsync</c> guards nothing, so a negative
    /// <c>activeSeconds</c> writes a row whose <c>EndTimestamp</c> precedes its <c>StartTimestamp</c> and
    /// whose <c>Length</c> is negative — a row no dashboard's arithmetic is prepared for, and one that
    /// subtracts from the day total <c>TotalSecondsBetweenAsync</c> feeds back to the completion automation.
    ///
    /// <para><b>Intended behaviour:</b> reject or clamp a negative <c>activeSeconds</c> at the sink.
    /// Not reachable from the desktop heartbeat today — <c>DesktopActivityHeartbeatValidator</c> applies
    /// <c>GreaterThanOrEqualTo(0)</c> to both second counts — so the guard is missing rather than actively
    /// broken. It is the seam's contract, not the one caller's validator, that ought to hold it: the sink
    /// is a Core interface any slice may call.</para>
    /// </summary>
    [Fact]
    [Trait("Status", "KnownGap")]
    public async Task AttributeAsync_WithNegativeSeconds_WritesAnInvertedRow()
    {
        await using var db = CreateDbContext();
        var activityId = await SeedActivityAsync(db, UserId);

        await Sink.AttributeAsync(db, UserId, activityId, TenAm, -100, CancellationToken);
        await db.SaveChangesAsync(CancellationToken);

        var row = await SingleRowAsync(activityId);
        row.EndTimestamp.Should().BeBefore(row.StartTimestamp,
            "pinning today's behaviour: nothing guards activeSeconds, so the interval inverts");
        row.Length.TotalSeconds.Should().BeNegative();
    }

    /// <summary>
    /// Attributing the <b>same</b> window twice is not contiguity — it is a duplicate, and
    /// <c>(UserId, ActivityId, StartTimestamp)</c> is unique. This documents which of the two outcomes the
    /// sink produces: a clean <see cref="DbUpdateException"/> on the caller's save, <b>not</b> a silent
    /// second row for the same instant.
    ///
    /// <para>The second call cannot see the first: the contiguity probe is a database query and the first
    /// row is still only tracked, so two <c>Added</c> entries reach the save. The heartbeat avoids this
    /// upstream with its <c>seenKeys</c> dedup (<c>CQ-8</c>) rather than relying on the sink.</para>
    /// </summary>
    [Fact]
    public async Task AttributeAsync_TwiceOnTheSameWindowStart_FailsTheUniqueIndex()
    {
        await using var db = CreateDbContext();
        var activityId = await SeedActivityAsync(db, UserId);

        await Sink.AttributeAsync(db, UserId, activityId, TenAm, 300, CancellationToken);
        await Sink.AttributeAsync(db, UserId, activityId, TenAm, 300, CancellationToken);

        var save = async () => await db.SaveChangesAsync(CancellationToken);

        await save.Should().ThrowAsync<DbUpdateException>(
            "a duplicate (UserId, ActivityId, StartTimestamp) must surface as an error the caller can " +
            "handle, not as two rows for one instant that every later total double-counts");
    }

    // ---- IntTime truncation: the new findings ----------------------------------------------------

    /// <summary>
    /// <b>New finding, pinned not fixed — and the one that matters.</b> <see cref="IntTime"/> stores only
    /// <c>Hours</c> and <c>Minutes</c>, so <c>new IntTime(seconds)</c> discards the sub-minute remainder.
    /// A 45-second attribution is therefore recorded as <b>zero</b>: the row exists, its
    /// <c>EndTimestamp</c> is honest, and its <c>Length</c> — the field every dashboard and
    /// <c>TotalSecondsBetweenAsync</c> read — is 0.
    ///
    /// <para>This is squarely on the live path. <c>DesktopActivityHeartbeatValidator</c> caps a heartbeat at
    /// 60 active seconds <i>across all entries</i>, so any window split over two applications hands the sink
    /// two sub-minute values, and <b>both are lost entirely</b>. Only a window where a single activity holds
    /// all 60 seconds survives.</para>
    ///
    /// <para><b>Intended behaviour:</b> the ledger should keep the seconds it was given. The stored column is
    /// already <c>int</c> seconds (<c>IntTimeConverter</c> writes <c>TotalSeconds</c>), so the loss is in the
    /// value object's constructor, not in the schema — which also means fixing it is a Framework change with
    /// solution-wide reach, not a History one. Hence pinned here rather than fixed.</para>
    /// </summary>
    [Fact]
    [Trait("Status", "KnownGap")]
    public async Task AttributeAsync_WithSubMinuteSeconds_RecordsNothing()
    {
        await using var db = CreateDbContext();
        var activityId = await SeedActivityAsync(db, UserId);

        await Sink.AttributeAsync(db, UserId, activityId, TenAm, 45, CancellationToken);
        await db.SaveChangesAsync(CancellationToken);

        var row = await SingleRowAsync(activityId);
        row.EndTimestamp.Should().Be(TenAm.AddSeconds(45), "the window's extent is recorded correctly");
        row.Length.TotalSeconds.Should().Be(0,
            "pinning today's behaviour: IntTime keeps only whole minutes, so 45 seconds becomes zero");

        var total = await Sink.TotalSecondsBetweenAsync(db, UserId, activityId, DayStart, DayEnd, CancellationToken);
        total.Should().Be(0, "and the day total the completion automation reads is zero too");
    }

    /// <summary>
    /// <b>New finding, pinned not fixed</b> — the compounding consequence of
    /// <see cref="AttributeAsync_WithSubMinuteSeconds_RecordsNothing"/>, and the reason it is worth a second
    /// test: it shows the extension rule is effectively dead for real heartbeats.
    ///
    /// <para><c>WindowStart</c> is validated to be minute-aligned, and <c>EndTimestamp</c> is
    /// <c>windowStart + activeSeconds</c>. So the next heartbeat's start can only equal the previous end when
    /// <c>activeSeconds</c> was exactly 60. Two consecutive 45-second windows produce <b>two</b> rows — never
    /// the extension the class exists for — and both carry a zero <c>Length</c>, so 90 seconds of tracked
    /// activity totals 0.</para>
    ///
    /// <para><b>Intended behaviour:</b> two rows here is arguably right (the windows genuinely are not
    /// contiguous — there is a 15-second idle gap), but the totals must not be zero. Fixing the truncation
    /// fixes this test's numbers; the row count stays 2.</para>
    /// </summary>
    [Fact]
    [Trait("Status", "KnownGap")]
    public async Task AttributeAsync_AcrossTwoSubMinuteHeartbeats_NeitherExtendsNorAccumulates()
    {
        await using var db = CreateDbContext();
        var activityId = await SeedActivityAsync(db, UserId);

        await Sink.AttributeAsync(db, UserId, activityId, TenAm, 45, CancellationToken);
        await Sink.AttributeAsync(db, UserId, activityId, TenAm.AddMinutes(1), 45, CancellationToken);
        await db.SaveChangesAsync(CancellationToken);

        await using var read = CreateDbContext();
        var rows = await read.Set<ActivityHistory>()
            .Where(h => h.ActivityId == activityId)
            .ToListAsync(CancellationToken);

        rows.Should().HaveCount(2,
            "a sub-minute window never ends on the minute boundary the next one starts at, so the " +
            "contiguous-window extension can only ever fire for a full 60 active seconds");

        var total = await Sink.TotalSecondsBetweenAsync(db, UserId, activityId, DayStart, DayEnd, CancellationToken);
        total.Should().Be(0, "pinning today's behaviour: 90 seconds of tracked activity totals zero");
    }

    // ---- AttributeSpanAsync: the Android sync's entry point (FO9) ----------------------------------

    /// <summary>
    /// The double-count rule: the part of a phone session a record of the same activity already covers is not
    /// added again, and what is left starts where that record ends, so it extends it.
    /// </summary>
    [Fact]
    public async Task AttributeSpanAsync_AddsOnlyWhatTheSameActivityDoesNotCover()
    {
        await using var db = CreateDbContext();
        var activityId = await SeedActivityAsync(db, UserId);
        await SeedHistoryAsync(db, UserId, activityId, TenAm, 20 * 60);

        var added = await Sink.AttributeSpanAsync(db, UserId, activityId, TenAm.AddMinutes(10), TenAm.AddMinutes(40), CancellationToken);
        await db.SaveChangesAsync(CancellationToken);

        added.Should().Be(20 * 60, "the first ten minutes of the session were already on the ledger");
        var row = await SingleRowAsync(activityId);
        row.StartTimestamp.Should().Be(TenAm);
        row.EndTimestamp.Should().Be(TenAm.AddMinutes(40));
        row.Length.TotalSeconds.Should().Be(40 * 60, "forty minutes happened, not fifty");
    }

    /// <summary>
    /// Only the same activity is subtracted. A stretch already on another activity is a second verdict about the
    /// same minutes — the ledger has always held that shape for hand-logged rows — and is left alone.
    /// </summary>
    [Fact]
    public async Task AttributeSpanAsync_DoesNotSubtractAnotherActivitysRecord()
    {
        await using var db = CreateDbContext();
        var activityId = await SeedActivityAsync(db, UserId);
        var otherActivityId = await SeedActivityAsync(db, UserId);
        await SeedHistoryAsync(db, UserId, otherActivityId, TenAm, 30 * 60);

        var added = await Sink.AttributeSpanAsync(db, UserId, activityId, TenAm, TenAm.AddMinutes(30), CancellationToken);
        await db.SaveChangesAsync(CancellationToken);

        added.Should().Be(30 * 60);
        (await SingleRowAsync(activityId)).Length.TotalSeconds.Should().Be(30 * 60);
    }

    /// <summary>
    /// Two overlapping spans in one batch — two phones, or two packages mapped to one activity — are attributed
    /// once. The first is still unsaved when the second arrives, so this only holds because the sink reads the
    /// change tracker as well as the table; without it the second call would also add a row on its own start.
    /// </summary>
    [Fact]
    public async Task AttributeSpanAsync_CountsTwoOverlappingUnsavedSpansOnce()
    {
        await using var db = CreateDbContext();
        var activityId = await SeedActivityAsync(db, UserId);

        var first = await Sink.AttributeSpanAsync(db, UserId, activityId, TenAm, TenAm.AddMinutes(30), CancellationToken);
        var second = await Sink.AttributeSpanAsync(db, UserId, activityId, TenAm.AddMinutes(15), TenAm.AddMinutes(45), CancellationToken);
        await db.SaveChangesAsync(CancellationToken);

        first.Should().Be(30 * 60);
        second.Should().Be(15 * 60);
        var row = await SingleRowAsync(activityId);
        row.EndTimestamp.Should().Be(TenAm.AddMinutes(45));
        row.Length.TotalSeconds.Should().Be(45 * 60);
    }

    /// <summary>
    /// A timer's row describes what the timer measured, so it is never extended (the same rule as
    /// <c>AttributeAsync</c>): phone time after a countdown becomes its own row, starting where the timer stopped.
    /// </summary>
    [Fact]
    public async Task AttributeSpanAsync_StartsANewRowAfterATimerRow_RatherThanExtendingIt()
    {
        await using var db = CreateDbContext();
        var activityId = await SeedActivityAsync(db, UserId);
        var session = new ActivityHistorySession { UserId = UserId, Kind = TimerSessionKind.Timer, Outcome = TimerSessionOutcome.Completed };
        db.Set<ActivityHistorySession>().Add(session);
        db.Set<ActivityHistory>().Add(new ActivityHistory
        {
            UserId = UserId,
            ActivityId = activityId,
            StartTimestamp = TenAm,
            EndTimestamp = TenAm.AddMinutes(25),
            Length = new IntTime(25 * 60),
            Session = session
        });
        await db.SaveChangesAsync(CancellationToken);

        var added = await Sink.AttributeSpanAsync(db, UserId, activityId, TenAm.AddMinutes(20), TenAm.AddMinutes(40), CancellationToken);
        await db.SaveChangesAsync(CancellationToken);

        added.Should().Be(15 * 60);
        await using var read = CreateDbContext();
        var rows = await read.Set<ActivityHistory>().Where(h => h.ActivityId == activityId)
            .OrderBy(h => h.StartTimestamp).ToListAsync(CancellationToken);
        rows.Should().HaveCount(2);
        rows[0].EndTimestamp.Should().Be(TenAm.AddMinutes(25), "the timer's own row is left as the timer measured it");
        rows[1].StartTimestamp.Should().Be(TenAm.AddMinutes(25));
        rows[1].Length.TotalSeconds.Should().Be(15 * 60);
    }

    /// <summary>
    /// A leftover under a minute that extends nothing would be stored as a zero-length row (the <c>IntTime</c>
    /// gap above), which is noise in History's list and nothing in any total — so it is not written.
    /// </summary>
    [Fact]
    public async Task AttributeSpanAsync_DropsASubMinutePieceThatExtendsNothing()
    {
        await using var db = CreateDbContext();
        var activityId = await SeedActivityAsync(db, UserId);
        await SeedHistoryAsync(db, UserId, activityId, TenAm, 30 * 60);

        var added = await Sink.AttributeSpanAsync(db, UserId, activityId, TenAm.AddSeconds(-30), TenAm.AddMinutes(30), CancellationToken);
        await db.SaveChangesAsync(CancellationToken);

        added.Should().Be(0);
        (await SingleRowAsync(activityId)).StartTimestamp.Should().Be(TenAm, "no row was added in front of it");
    }

    // ---- isolation -------------------------------------------------------------------------------

    /// <summary>
    /// Attribution and totals are scoped to the user id the caller passes. An activity belongs to exactly
    /// one user, so this seeds a user and an activity on each side rather than sharing an activity id.
    ///
    /// <para>The context these run on is <b>unfiltered</b> — <c>PostgresContainerFixture</c> gives its own
    /// DbContexts a separate model cache key precisely because they have no application service provider and
    /// therefore carry no user query filter at all. So a correct answer here can only come from the sink's
    /// own <c>UserId</c> predicate, which is exactly what its remarks say it is for: the caller passes the id
    /// it authenticated, and the sink must not attribute to whoever happens to be ambient.</para>
    /// </summary>
    [Fact]
    public async Task TotalSecondsBetweenAsync_IsScopedToTheUserIdPassedIn_NotTheAmbientOne()
    {
        await using var db = CreateDbContext();

        var userBId = await SeedSecondUserAsync($"attribution-{Guid.NewGuid():N}@test.com");
        var activityA = await SeedActivityAsync(db, UserId);
        var activityB = await SeedActivityAsync(db, userBId);

        await Sink.AttributeAsync(db, UserId, activityA, TenAm, 300, CancellationToken);
        await db.SaveChangesAsync(CancellationToken);

        (await Sink.TotalSecondsBetweenAsync(db, userBId, activityB, DayStart, DayEnd, CancellationToken))
            .Should().Be(0, "user B has been attributed nothing");

        await Sink.AttributeAsync(db, userBId, activityB, TenAm, 600, CancellationToken);
        await db.SaveChangesAsync(CancellationToken);

        (await Sink.TotalSecondsBetweenAsync(db, UserId, activityA, DayStart, DayEnd, CancellationToken))
            .Should().Be(300, "attributing to B must not move or grow A's total");
        (await Sink.TotalSecondsBetweenAsync(db, userBId, activityB, DayStart, DayEnd, CancellationToken))
            .Should().Be(600);
    }

    /// <summary>
    /// The mirror of the read case: a window contiguous with <i>another user's</i> row is not contiguous.
    /// Without the <c>UserId</c> predicate on the probe, B's heartbeat would extend A's row — writing one
    /// user's tracked time into another user's ledger, which no query filter downstream could undo.
    /// </summary>
    [Fact]
    public async Task AttributeAsync_DoesNotExtendAnotherUsersContiguousRow()
    {
        await using var db = CreateDbContext();

        var userBId = await SeedSecondUserAsync($"attribution-extend-{Guid.NewGuid():N}@test.com");
        var activityA = await SeedActivityAsync(db, UserId);
        var activityB = await SeedActivityAsync(db, userBId);

        await SeedHistoryAsync(db, UserId, activityA, TenAm, 300);

        await Sink.AttributeAsync(db, userBId, activityB, TenAm.AddSeconds(300), 180, CancellationToken);
        await db.SaveChangesAsync(CancellationToken);

        var rowA = await SingleRowAsync(activityA);
        rowA.EndTimestamp.Should().Be(TenAm.AddSeconds(300), "A's row must be untouched by B's heartbeat");
        rowA.Length.TotalSeconds.Should().Be(300);

        var rowB = await SingleRowAsync(activityB);
        rowB.StartTimestamp.Should().Be(TenAm.AddSeconds(300), "B gets its own row");
        rowB.Length.TotalSeconds.Should().Be(180);
    }

    // ---- seeding ---------------------------------------------------------------------------------

    /// <summary>Reads back the only row for an activity on a fresh context, so nothing asserted is coming
    /// out of the change tracker rather than out of Postgres.</summary>
    private async Task<ActivityHistory> SingleRowAsync(long activityId)
    {
        await using var read = CreateDbContext();
        var rows = await read.Set<ActivityHistory>()
            .Where(h => h.ActivityId == activityId)
            .ToListAsync(CancellationToken);

        rows.Should().ContainSingle();
        return rows[0];
    }

    private async Task<long> SeedSecondUserAsync(string email)
    {
        using var scope = Fixture.UnauthenticatedFactory.Services.CreateScope();
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
        var result = await userManager.CreateAsync(user, "Test@1234!");
        result.Succeeded.Should().BeTrue();
        return user.Id;
    }

    /// <summary>Guid-suffixed names throughout: the Postgres container is shared across the collection and
    /// both <c>Activity</c> and <c>ActivityRole</c> carry a unique index on <c>(UserId, Name)</c>.</summary>
    private async Task<long> SeedActivityAsync(DbContext db, long userId)
    {
        var suffix = Guid.NewGuid().ToString("N");

        var role = new ActivityRole { UserId = userId, Name = $"Attribution Role {suffix}", Color = "#112233" };
        db.Set<ActivityRole>().Add(role);
        await db.SaveChangesAsync(CancellationToken);

        var activity = new Activity { UserId = userId, Name = $"Attribution Activity {suffix}", RoleId = role.Id };
        db.Set<Activity>().Add(activity);
        await db.SaveChangesAsync(CancellationToken);
        return activity.Id;
    }

    /// <summary>Seeds a ledger row directly, bypassing the sink — so a test of the extension rule is seeded
    /// by something other than the code it is testing.</summary>
    private async Task SeedHistoryAsync(DbContext db, long userId, long activityId, DateTime startUtc, int seconds)
    {
        db.Set<ActivityHistory>().Add(new ActivityHistory
        {
            UserId = userId,
            ActivityId = activityId,
            StartTimestamp = startUtc,
            EndTimestamp = startUtc.AddSeconds(seconds),
            Length = new IntTime(seconds)
        });
        await db.SaveChangesAsync(CancellationToken);
    }
}
