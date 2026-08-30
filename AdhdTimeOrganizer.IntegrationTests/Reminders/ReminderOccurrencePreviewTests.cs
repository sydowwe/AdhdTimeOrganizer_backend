using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.Contracts.reminders;
using Sydowwe.Framework.Testing;
using Sydowwe.Reminders.domain.entity;
using Sydowwe.Reminders.domain.@enum;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Reminders;

/// <summary>
/// The registry inspector's occurrence preview, asserted on projected instants rather than on the route.
/// <para>
/// Every rule here fails silently if it drifts. The preview is <b>computed</b>, so a schedule that quietly
/// stops matching what the scan will actually do still returns a well-formed, confidently wrong list — and the
/// entire point of the panel is verification, which makes a wrong answer worse than no answer. Three of the
/// rules are invisible from the client: the dispatch-log skip, the past skip, and the <b>pending-but-overdue</b>
/// floor, which is the one an admin most often opens the inspector to see. And an empty list is never
/// self-explanatory — <c>Exhausted</c> and <c>ScheduleInvalid</c> render identically and mean opposite things,
/// so the reason code is load-bearing, not decoration.
/// </para>
/// <para>
/// Definitions are seeded directly rather than registered through <c>IReminderRegistry</c>: the assertion is on
/// the read-side projection of stored schedule columns, and two of the cases (an invalid stored cron, a
/// pending-but-overdue occurrence) are states registration deliberately refuses to create.
/// </para>
/// </summary>
[Collection("Postgres")]
public class ReminderOccurrencePreviewTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    private const string OwnerModule = "OccurrencePreviewTest";

    [Fact(DisplayName = "A weekly interval projects ascending occurrences and says the series continues")]
    public async Task Interval_ProjectsWindow_AndReportsMore()
    {
        var anchor = DateTimeOffset.UtcNow.AddDays(1);
        long id;
        await using (var seed = CreateDbContext())
            id = (await SeedAsync(seed, "weekly", d =>
            {
                d.ScheduleType = ReminderScheduleType.RecurringInterval;
                d.IntervalPreset = ReminderIntervalPreset.Weekly;
                d.AnchorDate = anchor;
                d.NextOccurrenceAt = anchor;
            })).Id;

        var preview = await ReadAsync(id, count: 4);

        var occurrences = Occurrences(preview);
        occurrences.Should().HaveCount(4);
        occurrences.Should().BeInAscendingOrder("the panel renders the list top-down as a chronology");
        occurrences[0].Should().BeCloseTo(anchor, TimeSpan.FromSeconds(1),
            "the first projected occurrence is the one the scan will act on next — the two read the same calculator");
        occurrences[3].Should().BeCloseTo(anchor.AddDays(21), TimeSpan.FromSeconds(1));

        preview.GetProperty("hasMore").GetBoolean().Should().BeTrue(
            "an endless weekly series continues past any window, and 'séria pokračuje ďalej' is the only honest footer");
        preview.GetProperty("emptyReason").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact(DisplayName = "An end date truncates the series inclusively, and the last window says nothing follows")]
    public async Task Interval_EndsAtIsInclusive()
    {
        var anchor = DateTimeOffset.UtcNow.AddDays(1);
        long id;
        await using (var seed = CreateDbContext())
            id = (await SeedAsync(seed, "ends-at", d =>
            {
                d.ScheduleType = ReminderScheduleType.RecurringInterval;
                d.IntervalPreset = ReminderIntervalPreset.Daily;
                d.AnchorDate = anchor;
                // Exactly the third occurrence: it fires, and nothing after it does.
                d.EndsAt = anchor.AddDays(2);
                d.NextOccurrenceAt = anchor;
            })).Id;

        var preview = await ReadAsync(id, count: 8);

        Occurrences(preview).Should().HaveCount(3,
            "an occurrence falling exactly on the end date fires; only one strictly after it is dropped");
        preview.GetProperty("hasMore").GetBoolean().Should().BeFalse(
            "a finite series and a truncated window are indistinguishable to the client without this flag");
    }

    [Fact(DisplayName = "A one-shot projects one entry per lead offset, and the offset is the only nudge marker")]
    public async Task OneShot_ProjectsOneEntryPerLeadOffset()
    {
        var due = DateTimeOffset.UtcNow.AddDays(30);
        long id;
        await using (var seed = CreateDbContext())
            id = (await SeedAsync(seed, "one-shot", d =>
            {
                d.ScheduleType = ReminderScheduleType.OneShot;
                d.DueAt = due;
                d.LeadOffsets =
                [
                    new ReminderLeadOffset { OffsetMinutes = -20160 }, // 14 days before
                    new ReminderLeadOffset { OffsetMinutes = -2880 }, // 2 days before
                    new ReminderLeadOffset { OffsetMinutes = 0 } // the deadline itself
                ];
            })).Id;

        var preview = await ReadAsync(id, count: 8);

        var entries = preview.GetProperty("entries").EnumerateArray().ToList();
        entries.Should().HaveCount(3, "a one-shot with two offsets has two distinct fire instants, not one");

        Offsets(preview).Should().Equal([-20160, -2880, null],
            "leadOffsetMinutes is the whole occurrence-vs-nudge discriminator: the 0 offset IS the deadline, " +
            "so it is reported as the schedule firing rather than as a nudge ahead of it");

        Occurrences(preview)[2].Should().BeCloseTo(due, TimeSpan.FromSeconds(1));
        preview.GetProperty("hasMore").GetBoolean().Should().BeFalse();
    }

    [Fact(DisplayName = "Instants already in the dispatch log are skipped — a rule no client can see")]
    public async Task AlreadyDispatchedOccurrencesAreSkipped()
    {
        var anchor = DateTimeOffset.UtcNow.AddDays(1);
        long id;
        await using (var seed = CreateDbContext())
        {
            var definition = await SeedAsync(seed, "dispatched", d =>
            {
                d.ScheduleType = ReminderScheduleType.RecurringInterval;
                d.IntervalPreset = ReminderIntervalPreset.Daily;
                d.AnchorDate = anchor;
                d.NextOccurrenceAt = anchor;
            });
            id = definition.Id;

            // The second occurrence has already been acted on; the ledger, not the schedule, is the truth.
            seed.Set<ReminderDispatch>().Add(new ReminderDispatch
            {
                ReminderDefinitionId = id,
                OccurrenceAt = anchor.AddDays(1),
                DispatchedAt = DateTimeOffset.UtcNow,
                Outcome = DispatchOutcome.Sent,
                CorrelationId = "occurrence-preview-test"
            });
            await seed.SaveChangesAsync(CancellationToken);
        }

        var occurrences = Occurrences(await ReadAsync(id, count: 3));

        occurrences[0].Should().BeCloseTo(anchor, TimeSpan.FromSeconds(1));
        occurrences[1].Should().BeCloseTo(anchor.AddDays(2), TimeSpan.FromSeconds(1),
            "the dispatched instant is skipped, exactly as the scan would skip it");
    }

    [Fact(DisplayName = "A pending but overdue occurrence still shows — it is what the admin came to find")]
    public async Task OverdueButPendingOccurrenceIsProjected()
    {
        // The overdue instant has to sit on the daily grid, or no floor could return it and the test would be
        // asserting on the fixture rather than on the rule.
        var overdue = DateTimeOffset.UtcNow.AddHours(-2);
        var anchor = overdue.AddDays(-10);
        long id;
        await using (var seed = CreateDbContext())
            id = (await SeedAsync(seed, "overdue", d =>
            {
                d.ScheduleType = ReminderScheduleType.RecurringInterval;
                d.IntervalPreset = ReminderIntervalPreset.Daily;
                d.AnchorDate = anchor;
                // The scanner has not picked this up yet: still owed, and strictly in the past.
                d.NextOccurrenceAt = overdue;
            })).Id;

        var occurrences = Occurrences(await ReadAsync(id, count: 3));

        occurrences[0].Should().BeCloseTo(overdue, TimeSpan.FromSeconds(1),
            "a 'now' floor would hide the overdue occurrence and preview the reminder as if it had moved on");
    }

    [Fact(DisplayName = "A paused reminder projects the series it would fire if resumed")]
    public async Task PausedReminderProjectsHypotheticalSeries()
    {
        var anchor = DateTimeOffset.UtcNow.AddDays(1);
        long id;
        await using (var seed = CreateDbContext())
            id = (await SeedAsync(seed, "paused", d =>
            {
                d.ScheduleType = ReminderScheduleType.RecurringInterval;
                d.IntervalPreset = ReminderIntervalPreset.Daily;
                d.AnchorDate = anchor;
                d.Status = ReminderStatus.Paused;
                // Pausing nulls the pending occurrence, so there is nothing but the schedule to project from.
                d.NextOccurrenceAt = null;
            })).Id;

        Occurrences(await ReadAsync(id, count: 3)).Should().HaveCount(3,
            "an empty list here would make the panel useless on precisely the reminders someone is deciding whether to resume");
    }

    [Theory(DisplayName = "A cancelled or completed reminder projects nothing, with the Terminal reason")]
    [InlineData(nameof(ReminderStatus.Cancelled))]
    [InlineData(nameof(ReminderStatus.Completed))]
    public async Task TerminalReminderProjectsNothing(string status)
    {
        var anchor = DateTimeOffset.UtcNow.AddDays(1);
        long id;
        await using (var seed = CreateDbContext())
            id = (await SeedAsync(seed, $"terminal-{status}", d =>
            {
                d.ScheduleType = ReminderScheduleType.RecurringInterval;
                d.IntervalPreset = ReminderIntervalPreset.Daily;
                d.AnchorDate = anchor;
                d.Status = Enum.Parse<ReminderStatus>(status);
            })).Id;

        var preview = await ReadAsync(id, count: 3);

        preview.GetProperty("entries").GetArrayLength().Should().Be(0,
            "terminality is lifecycle, not schedule math — the calculator is status-blind, so the endpoint decides this");
        preview.GetProperty("emptyReason").GetString().Should().Be("Terminal");
    }

    [Fact(DisplayName = "An unparseable cron is reported as ScheduleInvalid, not as an exhausted series or a 500")]
    public async Task InvalidCronIsReportedAsScheduleInvalid()
    {
        long id;
        await using (var seed = CreateDbContext())
            id = (await SeedAsync(seed, "bad-cron", d =>
            {
                d.ScheduleType = ReminderScheduleType.RecurringCron;
                // Five-field Unix cron against a six/seven-field Quartz evaluator: it can never fire, and the
                // evaluator throws rather than returning null when asked to advance it.
                d.Cron = "0 8 1 * *";
            })).Id;

        var preview = await ReadAsync(id, count: 3);

        preview.GetProperty("entries").GetArrayLength().Should().Be(0);
        preview.GetProperty("emptyReason").GetString().Should().Be("ScheduleInvalid",
            "'nothing left to fire' and 'this cannot be evaluated' look identical on screen and mean opposite things");
    }

    [Fact(DisplayName = "A valid Quartz cron projects UTC instants matching the expression")]
    public async Task ValidCronProjectsUtcInstants()
    {
        long id;
        await using (var seed = CreateDbContext())
            id = (await SeedAsync(seed, "good-cron", d =>
            {
                d.ScheduleType = ReminderScheduleType.RecurringCron;
                d.Cron = "0 0 8 1 * ?"; // 08:00 UTC on the 1st of each month
            })).Id;

        var occurrences = Occurrences(await ReadAsync(id, count: 3));

        occurrences.Should().HaveCount(3);
        occurrences.Should().AllSatisfy(o =>
        {
            var utc = o.ToUniversalTime();
            utc.Day.Should().Be(1);
            utc.Hour.Should().Be(8, "the evaluator is pinned to UTC — a browser reimplementation would drift by its own offset");
            utc.Minute.Should().Be(0);
        });
    }

    [Fact(DisplayName = "A fully dispatched one-shot is Exhausted, not ScheduleInvalid")]
    public async Task FullyDispatchedOneShotIsExhausted()
    {
        var due = DateTimeOffset.UtcNow.AddDays(5);
        long id;
        await using (var seed = CreateDbContext())
        {
            var definition = await SeedAsync(seed, "exhausted", d =>
            {
                d.ScheduleType = ReminderScheduleType.OneShot;
                d.DueAt = due;
                d.LeadOffsets = [new ReminderLeadOffset { OffsetMinutes = 0 }];
            });
            id = definition.Id;

            seed.Set<ReminderDispatch>().Add(new ReminderDispatch
            {
                ReminderDefinitionId = id,
                OccurrenceAt = due,
                DispatchedAt = DateTimeOffset.UtcNow,
                Outcome = DispatchOutcome.Sent,
                CorrelationId = "occurrence-preview-test"
            });
            await seed.SaveChangesAsync(CancellationToken);
        }

        (await ReadAsync(id, count: 3)).GetProperty("emptyReason").GetString().Should().Be("Exhausted",
            "the schedule is fine and simply has nothing left — the opposite diagnosis from an invalid one");
    }

    [Fact(DisplayName = "count above the cap is a 400, never a silent truncation")]
    public async Task CountAboveTheCapIsRejected()
    {
        long id;
        await using (var seed = CreateDbContext())
            id = (await SeedAsync(seed, "cap", d =>
            {
                d.ScheduleType = ReminderScheduleType.RecurringInterval;
                d.IntervalPreset = ReminderIntervalPreset.Daily;
                d.AnchorDate = DateTimeOffset.UtcNow.AddDays(1);
            })).Id;

        var response = await CreateAdminRoleClient().GetAsync($"api/reminder-definition/{id}/occurrences?count=500", CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "a truncation the client cannot detect is the dangerous failure — the batch endpoint made the same choice");
    }

    [Fact(DisplayName = "404 means an unknown reminder id and nothing else")]
    public async Task UnknownIdIs404()
    {
        var response = await CreateAdminRoleClient().GetAsync("api/reminder-definition/99999999/occurrences", CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "the client separates 'no such reminder' from 'endpoint not deployed' on this status alone");
    }

    [Fact(DisplayName = "The preview is admin surface, like the rest of the registry inspector")]
    public async Task PlainUsersAreDenied()
    {
        long id;
        await using (var seed = CreateDbContext())
            id = (await SeedAsync(seed, "auth", d =>
            {
                d.ScheduleType = ReminderScheduleType.RecurringInterval;
                d.IntervalPreset = ReminderIntervalPreset.Daily;
                d.AnchorDate = DateTimeOffset.UtcNow.AddDays(1);
            })).Id;

        var response = await CreateUserRoleClient().GetAsync($"api/reminder-definition/{id}/occurrences", CancellationToken);

        response.StatusCode.Should().BeOneOf(HttpStatusCode.Forbidden, HttpStatusCode.Unauthorized);
    }

    // ---- helpers -----------------------------------------------------------------------------------

    private async Task<JsonElement> ReadAsync(long id, int? count = null)
    {
        var url = $"api/reminder-definition/{id}/occurrences" + (count is null ? string.Empty : $"?count={count}");
        var response = await CreateAdminRoleClient().GetAsync(url, CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return await response.Content.ReadFromJsonAsync<JsonElement>(JsonOpts, CancellationToken);
    }

    private static List<DateTimeOffset> Occurrences(JsonElement preview) =>
        preview.GetProperty("entries").EnumerateArray().Select(e => e.GetProperty("occursAt").GetDateTimeOffset()).ToList();

    private static List<int?> Offsets(JsonElement preview) =>
        preview.GetProperty("entries").EnumerateArray()
            .Select(e => e.GetProperty("leadOffsetMinutes") is { ValueKind: JsonValueKind.Null } ? (int?)null : e.GetProperty("leadOffsetMinutes").GetInt32())
            .ToList();

    private async Task<ReminderDefinition> SeedAsync(DbContext db, string kind, Action<ReminderDefinition> configure)
    {
        var definition = new ReminderDefinition
        {
            OwnerModule = OwnerModule,
            SubjectType = "Fixture",
            // The key is uniquely indexed and these tests share a database, so every run needs its own subject.
            SubjectId = Guid.NewGuid().ToString("N"),
            Kind = kind,
            TemplateKey = "occurrence.fixture",
            ScheduleType = ReminderScheduleType.OneShot,
            RecipientMode = RecipientMode.ExplicitUsers,
            Status = ReminderStatus.Active
        };
        configure(definition);

        db.Set<ReminderDefinition>().Add(definition);
        await db.SaveChangesAsync(CancellationToken);
        return definition;
    }
}
