using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.Contracts.reminders;
using Sydowwe.Framework.Testing;
using Sydowwe.Reminders.application.dto.reminderDefinition;
using Sydowwe.Reminders.domain.entity;
using Sydowwe.Reminders.domain.@enum;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Reminders;

/// <summary>
/// The batch lifecycle route (<c>POST api/reminder-definition/batch-lifecycle</c>), which replaces an admin
/// client's fan-out of N single-key requests over the reminder register.
/// <para>
/// Everything here asserts on <b>rows</b>, not on the route: the whole point of the endpoint is which
/// definitions actually moved and which provably did not, and every one of those claims is invisible to a
/// status-code assertion. A batch that reported three successes while writing one row would pass a route test
/// and be a data-loss bug — and the promise the cancel confirmation dialog makes to the user
/// ("this cannot be undone") is only true if a batch that fails partway leaves no third state behind.
/// </para>
/// <para>
/// Definitions are seeded straight into the module table rather than registered through the portal, because
/// three of these cases (an already-Paused row, a Cancelled row, a row whose stored cron is invalid) are
/// states <c>RegisterAsync</c> will not produce on demand — the invalid cron in particular is rejected at
/// registration, which is exactly why a row that has one can only have arrived some other way.
/// </para>
/// </summary>
[Collection("Postgres")]
public class ReminderBatchLifecycleTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    private const string Url = "api/reminder-definition/batch-lifecycle";
    private const string OwnerModule = "BatchTest";
    private const string SubjectType = "Subject";
    private const string Kind = "Nudge";

    // ---- the mixed selection, which is the normal case on the register ---------------------------

    [Fact(DisplayName = "A mixed pause batch moves only the active rows and reports each key's own outcome")]
    public async Task Pause_MixedSelection_AppliesPerKeyAndLeavesTheRestUntouched()
    {
        var active = await SeedAsync("active", ReminderStatus.Active);
        var paused = await SeedAsync("paused", ReminderStatus.Paused);
        var cancelled = await SeedAsync("cancelled", ReminderStatus.Cancelled);

        var response = await CreateClient().PostAsJsonAsync(Url, new
        {
            Action = nameof(ReminderLifecycleAction.Pause),
            Keys = new[] { KeyFor("active"), KeyFor("paused"), KeyFor("cancelled"), KeyFor("no-such-subject") }
        }, JsonOpts, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "a batch in which some keys fail is still a successful batch — the outcomes carry the failures");

        var results = await ReadResultsAsync(response);
        results.Should().HaveCount(4, "one outcome per requested key, in request order");

        AssertOutcome(results[0], "active", ReminderLifecycleOutcome.Applied, null);
        AssertOutcome(results[1], "paused", ReminderLifecycleOutcome.NoChange, ReminderLifecycleReason.AlreadyInState);
        AssertOutcome(results[2], "cancelled", ReminderLifecycleOutcome.NoChange, ReminderLifecycleReason.TerminalState);
        AssertOutcome(results[3], "no-such-subject", ReminderLifecycleOutcome.Failed, ReminderLifecycleReason.NotFound);

        await using var db = CreateDbContext();
        (await StatusOfAsync(db, active)).Should().Be(ReminderStatus.Paused);
        (await StatusOfAsync(db, paused)).Should().Be(ReminderStatus.Paused);
        (await StatusOfAsync(db, cancelled)).Should().Be(ReminderStatus.Cancelled,
            "a NoChange must not have written anything — a cancelled reminder that came back as paused would be resurrected by a later resume");

        var pausedRow = await db.Set<ReminderDefinition>().SingleAsync(d => d.Id == active, CancellationToken);
        pausedRow.NextOccurrenceAt.Should().BeNull("a paused reminder must drop out of the scanner's Status = Active AND NextOccurrenceAt <= now seek");
    }

    [Fact(DisplayName = "An idempotent no-op is reported as NoChange, not as an action taken")]
    public async Task Pause_AlreadyPaused_IsNotCountedAsApplied()
    {
        await SeedAsync("already-paused", ReminderStatus.Paused);

        var response = await CreateClient().PostAsJsonAsync(Url, new
        {
            Action = nameof(ReminderLifecycleAction.Pause),
            Keys = new[] { KeyFor("already-paused") }
        }, JsonOpts, CancellationToken);

        var results = await ReadResultsAsync(response);
        results[0].GetProperty("outcome").GetString().Should().Be(nameof(ReminderLifecycleOutcome.NoChange),
            "reporting this as Applied would inflate the admin's success count with reminders nothing happened to");
        results[0].GetProperty("message").GetString().Should().NotBeNullOrWhiteSpace(
            "the dialog renders this as the per-row subtitle");
    }

    // ---- failure isolation, and what a failure is allowed to say ---------------------------------

    [Fact(DisplayName = "A reminder whose stored cron is invalid fails its own key without stack text, and its neighbours still apply")]
    public async Task Resume_InvalidStoredCron_FailsOneKeyAndIsolatesIt()
    {
        var healthy = await SeedAsync("healthy", ReminderStatus.Paused);
        var broken = await SeedAsync("broken-cron", ReminderStatus.Paused, def =>
        {
            def.ScheduleType = ReminderScheduleType.RecurringCron;
            def.Cron = "0 0 12 * *"; // one field short — Quartz throws a FormatException parsing it
            def.DueAt = null;
        });

        var response = await CreateClient().PostAsJsonAsync(Url, new
        {
            Action = nameof(ReminderLifecycleAction.Resume),
            Keys = new[] { KeyFor("broken-cron"), KeyFor("healthy") }
        }, JsonOpts, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK, "one unusable reminder must not take the batch down with it");

        var results = await ReadResultsAsync(response);
        AssertOutcome(results[0], "broken-cron", ReminderLifecycleOutcome.Failed, ReminderLifecycleReason.InvalidSchedule);
        AssertOutcome(results[1], "healthy", ReminderLifecycleOutcome.Applied, null);

        var body = await response.Content.ReadAsStringAsync(CancellationToken);
        body.Should().NotContain("System.", "an admin must never be shown an exception type");
        body.Should().NotContain("Quartz", "nor the internals of the cron engine");
        body.Should().NotContain("   at ", "nor a stack frame");

        await using var db = CreateDbContext();
        (await StatusOfAsync(db, broken)).Should().Be(ReminderStatus.Paused,
            "a failed resume must leave the row exactly as it was — a half-applied resume would be a reminder that is Active with no next occurrence, i.e. silently never dispatched again");
        (await StatusOfAsync(db, healthy)).Should().Be(ReminderStatus.Active,
            "the sibling key is independent; isolating the failure means the rest of the batch still lands");
    }

    [Fact(DisplayName = "The single-key resume route answers an unusable schedule with a 400, not a 500 carrying the cron engine's stack")]
    public async Task SingleResume_InvalidStoredCron_Returns400WithoutExceptionText()
    {
        await SeedAsync("single-broken", ReminderStatus.Paused, def =>
        {
            def.ScheduleType = ReminderScheduleType.RecurringCron;
            def.Cron = "0 0 12 * *";
            def.DueAt = null;
        });

        var response = await CreateClient().PostAsJsonAsync(
            "api/reminder-definition/resume", KeyFor("single-broken"), JsonOpts, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "this used to escape as an unhandled FormatException: a 500 whose body was the developer exception page in Development and empty in Production");

        var body = await response.Content.ReadAsStringAsync(CancellationToken);
        body.Should().NotContain("Quartz");
        body.Should().NotContain("   at ");
    }

    // ---- the batch-level rejections, which must touch nothing -------------------------------------

    [Fact(DisplayName = "Over the size cap the batch is refused whole — never silently truncated")]
    public async Task OverMaxKeys_Returns400AndAppliesNothing()
    {
        var seeded = await SeedAsync("cap-canary", ReminderStatus.Active);

        var keys = Enumerable.Range(0, BatchReminderLifecycleRequest.MaxKeys + 1)
            .Select(i => KeyFor(i == 0 ? "cap-canary" : $"filler-{i}"))
            .ToArray();

        var response = await CreateClient().PostAsJsonAsync(Url, new
        {
            Action = nameof(ReminderLifecycleAction.Pause),
            Keys = keys
        }, JsonOpts, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        await using var db = CreateDbContext();
        (await StatusOfAsync(db, seeded)).Should().Be(ReminderStatus.Active,
            "a truncation the client cannot detect is the dangerous failure — over the cap, nothing may be applied at all");
    }

    [Fact(DisplayName = "A duplicate key is refused rather than deduplicated — the client matches outcomes by key")]
    public async Task DuplicateKey_Returns400()
    {
        await SeedAsync("dupe", ReminderStatus.Active);

        var response = await CreateClient().PostAsJsonAsync(Url, new
        {
            Action = nameof(ReminderLifecycleAction.Cancel),
            Keys = new[] { KeyFor("dupe"), KeyFor("dupe") }
        }, JsonOpts, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact(DisplayName = "An empty key list is refused")]
    public async Task EmptyKeys_Returns400()
    {
        var response = await CreateClient().PostAsJsonAsync(Url, new
        {
            Action = nameof(ReminderLifecycleAction.Pause),
            Keys = Array.Empty<object>()
        }, JsonOpts, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ---- the gate ---------------------------------------------------------------------------------

    [Fact(DisplayName = "The batch route is Admin-only, like the three single routes it batches")]
    public async Task PlainUser_IsRefused()
    {
        var seeded = await SeedAsync("gate", ReminderStatus.Active);

        var response = await CreateUserRoleClient().PostAsJsonAsync(Url, new
        {
            Action = nameof(ReminderLifecycleAction.Cancel),
            Keys = new[] { KeyFor("gate") }
        }, JsonOpts, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "the key is client-supplied and the registry has no notion of a caller, so the role gate is the whole boundary");

        await using var db = CreateDbContext();
        (await StatusOfAsync(db, seeded)).Should().Be(ReminderStatus.Active,
            "a refused batch must not have reached the registry — asserting the 403 alone would not catch a gate that runs after the writes");
    }

    [Fact(DisplayName = "An anonymous caller is refused")]
    public async Task Anonymous_Returns401()
    {
        var response = await CreateUnauthenticatedClient().PostAsJsonAsync(Url, new
        {
            Action = nameof(ReminderLifecycleAction.Pause),
            Keys = new[] { KeyFor("whatever") }
        }, JsonOpts, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ---- fixtures ---------------------------------------------------------------------------------

    private async Task<long> SeedAsync(string subjectId, ReminderStatus status, Action<ReminderDefinition>? customize = null)
    {
        await using var db = CreateDbContext();
        var definition = new ReminderDefinition
        {
            OwnerModule = OwnerModule,
            SubjectType = SubjectType,
            SubjectId = subjectId,
            Kind = Kind,
            TemplateKey = "batch.test",
            ScheduleType = ReminderScheduleType.OneShot,
            DueAt = DateTimeOffset.UtcNow.AddDays(2),
            RecipientMode = RecipientMode.ExplicitUsers,
            Status = status,
            NextOccurrenceAt = status == ReminderStatus.Active ? DateTimeOffset.UtcNow.AddDays(2) : null
        };
        customize?.Invoke(definition);

        db.Set<ReminderDefinition>().Add(definition);
        await db.SaveChangesAsync(CancellationToken);
        return definition.Id;
    }

    private static object KeyFor(string subjectId) => new
    {
        OwnerModule,
        SubjectType,
        SubjectId = subjectId,
        Kind
    };

    private async Task<JsonElement[]> ReadResultsAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(CancellationToken);
        return body.GetProperty("results").EnumerateArray().ToArray();
    }

    private static void AssertOutcome(
        JsonElement result, string subjectId, ReminderLifecycleOutcome outcome, ReminderLifecycleReason? reason)
    {
        result.GetProperty("key").GetProperty("subjectId").GetString().Should().Be(subjectId,
            "the key is echoed so the client can match the outcome back to its table row");
        result.GetProperty("outcome").GetString().Should().Be(outcome.ToString());

        if (reason is null)
        {
            result.GetProperty("reason").ValueKind.Should().Be(JsonValueKind.Null);
            return;
        }

        result.GetProperty("reason").GetString().Should().Be(reason.ToString(),
            "the client groups and localizes on the code, so it has to be stable");
        result.GetProperty("message").GetString().Should().NotBeNullOrWhiteSpace();
    }

    private Task<ReminderStatus> StatusOfAsync(DbContext db, long definitionId) =>
        db.Set<ReminderDefinition>()
            .Where(d => d.Id == definitionId)
            .Select(d => d.Status)
            .SingleAsync(CancellationToken);
}
