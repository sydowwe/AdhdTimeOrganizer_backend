using System.Net;
using System.Net.Http.Json;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using AdhdTimeOrganizer.Planning.domain.model.@enum;
using AdhdTimeOrganizer.Planning.domain.model.entity.activityPlanning;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.Testing;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Endpoints;

/// <summary>
/// TEST-11 / <c>CQ-8</c> — <c>RepeatingPlannerTaskValidator</c> is the slice's most intricate validator (four
/// recurrence shapes, each with its own required collection) and it was the only one whose two CRUD endpoints
/// never wired it: <c>CreateRepeatingPlannerTaskEndpoint</c> and <c>UpdateRepeatingPlannerTaskEndpoint</c> were
/// bare one-line subclasses with no <c>Configure()</c> override. Nothing in the suite settled whether
/// FastEndpoints was binding it by convention anyway, so the eighty lines of recurrence rules were either dead
/// code or untested code and the two were indistinguishable from outside.
/// <para>
/// <b>The answer, measured 2026-09-08:</b> untested, not dead. Every case below is green with the two
/// <c>Configure()</c> overrides removed as well as with them, so FastEndpoints was binding
/// <c>Validator&lt;RepeatingPlannerTaskRequest&gt;</c> by convention from the scanned assemblies all along and
/// the recurrence rules did run. The overrides stay anyway, for the reason <c>CQ-8</c> gives: the other
/// eleven endpoints in the slice wire theirs explicitly, so leaving these two on the convention makes them
/// the ones that break silently if a second validator for this request ever appears — the convention binds
/// only when there is exactly one.
/// </para>
/// <para>
/// This file is the table that measured it: one case per rule in the validator, run against
/// <c>POST</c> and against <c>PUT</c>, because the two endpoints had the same gap. The controls — a
/// well-formed request of each of the four recurrence types — are what stops the guard regressing into
/// "reject everything", which a rule accidentally applied outside its <c>When</c> would look exactly like.
/// </para>
/// <para>
/// Every rejection is asserted on the <b>row</b> as well as the status: a create must leave the table as it
/// was, and an update must leave the target row's span and recurrence untouched. A 400 with the write already
/// committed is the failure this shape catches and a bare status assertion does not.
/// </para>
/// <para>
/// The end-before-start cases belong to <c>CQ-14</c>, which added the ordering rule this validator was one of
/// three to lack. They are here rather than in <c>PlannerTaskStatusAndSpanTests</c> because a recurrence
/// materialises into ordinary planner tasks, so the shape it seeds is this endpoint's to refuse.
/// </para>
/// </summary>
[Collection("Postgres")]
public class RepeatingPlannerTaskValidationTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    private const string Route = "api/repeating-planner-task";

    private long _activityId;
    private long _importanceId;
    private long _existingId;

    protected override async Task SeedAsync(DbContext db)
    {
        _activityId = await PlanningTestSeedHelper.SeedActivityAsync(db, "repeating-validation");
        _importanceId = await PlanningTestSeedHelper.SeedTaskImportanceAsync(db, 5);
        _existingId = await PlanningTestSeedHelper.SeedRepeatingPlannerTaskAsync(db, _activityId);
    }

    /// <summary>One name per rule in <c>RepeatingPlannerTaskValidator</c>; <see cref="Malformed"/> builds each.</summary>
    public static TheoryData<string> MalformedCases =>
    [
        "DayOfWeek/no days at all",
        "DayOfWeek/Funday is not a day",
        "DayOfWeek/day names are exact-case",
        "DayOfMonth/no dates at all",
        "DayOfMonth/day 0",
        "DayOfMonth/day 32",
        "DateRange/no from-date",
        "DateRange/no to-date",
        "DateRange/from after to",
        "DayType/no day types at all",
        "DayType/Holiday is not a DayType",
        "any/no importance",
        "any/activity id 0",
        "any/location over 200 chars",
        "any/notes over 1000 chars",
        "any/start hour 24",
        "any/end before start",
        "any/end equal to start"
    ];

    public static TheoryData<string> RecurrenceTypes => ["DayOfWeek", "DayOfMonth", "DateRange", "DayType"];

    [Theory(DisplayName = "POST refuses a malformed recurrence and writes no row")]
    [MemberData(nameof(MalformedCases))]
    public async Task Post_RefusesAMalformedRecurrence(string caseName)
    {
        var response = await CreateUserRoleClient()
            .PostAsJsonAsync(Route, Malformed(caseName), JsonOpts, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "the validator owns this rule — a 500 here means ToEntity threw instead, and a 201 that it never ran");

        await using var db = CreateDbContext();
        var count = await db.Set<RepeatingPlannerTask>().CountAsync(CancellationToken);
        count.Should().Be(1, "only the row SeedAsync created should exist — the rejected request must not have written");
    }

    [Theory(DisplayName = "PUT refuses a malformed recurrence and leaves the row as it was")]
    [MemberData(nameof(MalformedCases))]
    public async Task Put_RefusesAMalformedRecurrence(string caseName)
    {
        var response = await CreateUserRoleClient()
            .PutAsJsonAsync($"{Route}/{_existingId}", Malformed(caseName), JsonOpts, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        await using var db = CreateDbContext();
        var row = await db.Set<RepeatingPlannerTask>().SingleAsync(t => t.Id == _existingId, CancellationToken);
        row.StartTime.Should().Be(new TimeOnly(9, 0), "the seeded span must survive a rejected update");
        row.EndTime.Should().Be(new TimeOnly(10, 0));
        row.RecurrenceType.Should().Be(RecurrenceType.DayOfWeek);
        row.ScheduledDays.Should().BeEmpty("the seeded row has no days — a partially applied update would have given it some");
    }

    [Theory(DisplayName = "POST accepts a well-formed recurrence of each of the four types")]
    [MemberData(nameof(RecurrenceTypes))]
    public async Task Post_AcceptsAWellFormedRecurrence(string recurrenceType)
    {
        var response = await CreateUserRoleClient()
            .PostAsJsonAsync(Route, WellFormed(recurrenceType), JsonOpts, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Created,
            "a rule firing outside its own When() would look exactly like the guard working");

        var id = await response.Content.ReadFromJsonAsync<long>(CancellationToken);
        await using var db = CreateDbContext();
        var row = await db.Set<RepeatingPlannerTask>().SingleAsync(t => t.Id == id, CancellationToken);
        row.RecurrenceType.Should().Be(Enum.Parse<RecurrenceType>(recurrenceType));
    }

    [Theory(DisplayName = "PUT accepts a well-formed recurrence of each of the four types")]
    [MemberData(nameof(RecurrenceTypes))]
    public async Task Put_AcceptsAWellFormedRecurrence(string recurrenceType)
    {
        var response = await CreateUserRoleClient()
            .PutAsJsonAsync($"{Route}/{_existingId}", WellFormed(recurrenceType), JsonOpts, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        await using var db = CreateDbContext();
        var row = await db.Set<RepeatingPlannerTask>().SingleAsync(t => t.Id == _existingId, CancellationToken);
        row.RecurrenceType.Should().Be(Enum.Parse<RecurrenceType>(recurrenceType));
        row.StartTime.Should().Be(new TimeOnly(7, 0), "the accepted update must actually have been applied");
    }

    /// <summary>
    /// The request the four recurrence types each need to be accepted: every shape carries only the collection
    /// its own <c>When</c> clause looks at, so a rule leaking outside its recurrence type fails one of these.
    /// </summary>
    private Dictionary<string, object?> WellFormed(string recurrenceType) => new()
    {
        ["activityId"] = _activityId,
        ["importanceId"] = _importanceId,
        ["startTime"] = new { hours = 7, minutes = 0 },
        ["endTime"] = new { hours = 8, minutes = 0 },
        ["isBackground"] = false,
        ["isActive"] = true,
        ["recurrenceType"] = recurrenceType,
        ["scheduledDays"] = recurrenceType == "DayOfWeek" ? new[] { "Monday" } : Array.Empty<string>(),
        ["scheduledDates"] = recurrenceType == "DayOfMonth" ? new[] { 1 } : Array.Empty<int>(),
        ["activeFromDate"] = recurrenceType == "DateRange" ? "2027-01-01" : null,
        ["activeToDate"] = recurrenceType == "DateRange" ? "2027-12-31" : null,
        ["scheduledForDayTypes"] = recurrenceType == "DayType" ? new[] { "Workday" } : Array.Empty<string>(),
        ["location"] = null,
        ["notes"] = null
    };

    private Dictionary<string, object?> Malformed(string caseName)
    {
        var body = WellFormed(caseName.Split('/')[0] is "any" ? "DayOfWeek" : caseName.Split('/')[0]);

        switch (caseName)
        {
            case "DayOfWeek/no days at all":
                body["scheduledDays"] = Array.Empty<string>();
                break;
            case "DayOfWeek/Funday is not a day":
                body["scheduledDays"] = new[] { "Funday" };
                break;
            // ValidDayNames is a string[] and Contains is ordinal, so the check is exact-case. Pinning the
            // answer the code gives rather than the one a caller might hope for: the day names are also what
            // GetSuggestionsRepeatingPlannerTaskEndpoint matches against, and it compares the same way.
            case "DayOfWeek/day names are exact-case":
                body["scheduledDays"] = new[] { "monday" };
                break;
            case "DayOfMonth/no dates at all":
                body["scheduledDates"] = Array.Empty<int>();
                break;
            case "DayOfMonth/day 0":
                body["scheduledDates"] = new[] { 0 };
                break;
            case "DayOfMonth/day 32":
                body["scheduledDates"] = new[] { 32 };
                break;
            case "DateRange/no from-date":
                body["activeFromDate"] = null;
                break;
            case "DateRange/no to-date":
                body["activeToDate"] = null;
                break;
            case "DateRange/from after to":
                body["activeFromDate"] = "2027-12-31";
                body["activeToDate"] = "2027-01-01";
                break;
            case "DayType/no day types at all":
                body["scheduledForDayTypes"] = Array.Empty<string>();
                break;
            // DayType has five members and no Holiday (DOC-2): a public holiday is a Workday row carrying a
            // HolidayName, so it is not a value this collection can ever hold.
            case "DayType/Holiday is not a DayType":
                body["scheduledForDayTypes"] = new[] { "Holiday" };
                break;
            // ImportanceId is NotNull().GreaterThan(0) here and merely optional on the other two task shapes,
            // so this is the one rule a reader porting from PlannerTaskValidator would expect to pass.
            case "any/no importance":
                body["importanceId"] = null;
                break;
            case "any/activity id 0":
                body["activityId"] = 0;
                break;
            case "any/location over 200 chars":
                body["location"] = new string('x', 201);
                break;
            case "any/notes over 1000 chars":
                body["notes"] = new string('x', 1001);
                break;
            // The one case that would not merely be accepted if the validator stopped running: ToEntity calls
            // TimeDto.ToTimeOnly(), which is new TimeOnly(24, 0) and throws, so it would come back a 500.
            // That makes it the cheapest single case to watch if this ever regresses.
            case "any/start hour 24":
                body["startTime"] = new { hours = 24, minutes = 0 };
                break;
            case "any/end before start":
                body["endTime"] = new { hours = 6, minutes = 0 };
                break;
            // The ordering rule is strictly-before, so a zero-length recurrence is refused too.
            case "any/end equal to start":
                body["endTime"] = new { hours = 7, minutes = 0 };
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(caseName), caseName, "no such case");
        }

        return body;
    }
}
