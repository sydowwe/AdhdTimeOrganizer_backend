using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AdhdTimeOrganizer.ActivityProfiles.domain.model.entity;
using AdhdTimeOrganizer.ActivityProfiles.domain.model.@enum;
using AdhdTimeOrganizer.Core.domain.model.entity.activity;
using AdhdTimeOrganizer.Core.domain.model.entity.@base;
using AdhdTimeOrganizer.Core.domain.model.entity.user;
using AdhdTimeOrganizer.History.domain.model.entity.activityHistory;
using AdhdTimeOrganizer.infrastructure.persistence;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using AdhdTimeOrganizer.Tracking.domain.model.entity.activityTracking;
using AdhdTimeOrganizer.Tracking.domain.model.entity.activityTracking.desktop;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sydowwe.Framework.domain.@enum;
using Sydowwe.Framework.domain.helper;
using Sydowwe.Framework.domain.valueObject;
using Sydowwe.Framework.Testing;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Endpoints;

/// <summary>
/// <c>GET /user/account-deletion-summary</c> — the numbers behind the SPA's deletion warning card.
///
/// <para>Every failure mode of this endpoint is silent. A count wired to the wrong <c>DbSet</c>, a subquery
/// deleted along with the slice extraction that moved its entity, a filter quietly capping the tracking
/// numbers at the partition window, a span read on the server's clock rather than the account's — none of
/// them throw, none of them log, and all of them still answer 200 with a plausible-looking body. So these
/// tests assert on the numbers against seeded rows, never on the route answering, and they seed a
/// <b>different count into every category</b> so that a subquery pointed at the neighbouring table fails
/// here instead of shipping.</para>
/// </summary>
[Collection("Postgres")]
public class AccountDeletionSummaryTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    private const string Route = "api/user/account-deletion-summary";
    private const string Password = "Test@1234!";

    // ─── the route itself ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Summary_Unauthenticated_Returns401()
    {
        var response = await CreateUnauthenticatedClient().GetAsync(Route, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Summary_ForAnUntouchedAccount_IsAllZeroesAndReportsNoSpan()
    {
        var callerId = await SeedUserAsync($"deletion-empty-{Guid.NewGuid():N}@test.com");

        var body = await GetSummaryAsync(callerId);

        foreach (var field in CountFields)
            Count(body, field).Should().Be(0, $"a brand-new account has no {field}");

        body.GetProperty("trackedFrom").ValueKind.Should().Be(JsonValueKind.Null);
        body.GetProperty("trackedTo").ValueKind.Should().Be(JsonValueKind.Null);
        body.GetProperty("trackedTimeSpanDays").ValueKind.Should().Be(JsonValueKind.Null,
            "a span of zero days would read as 'no history' next to a session count that is also zero — " +
            "null is what tells the card to say nothing at all");
        body.GetProperty("googleCalendarLinked").GetBoolean().Should().BeFalse();
    }

    // ─── the completeness guard ──────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The load-bearing test. One category at a time is what breaks: the endpoint is a single projection of
    /// fifteen independent subqueries, and deleting or mis-aiming any one of them compiles, returns 200, and
    /// simply under-reports what the user is about to lose — which is the only thing this card exists to say.
    /// Each category is seeded with a deliberately distinct row count, so a subquery reading the wrong table
    /// lands on the wrong number rather than coincidentally on the right one.
    /// </summary>
    [Fact]
    public async Task Summary_CountsEveryCategoryItClaims_WithNoneReadingAnothersTable()
    {
        var callerId = await SeedUserAsync($"deletion-full-{Guid.NewGuid():N}@test.com");

        await using var db = CreateDbContext();
        await SeedEveryCategoryAsync(db, callerId);
        await LinkGoogleCalendarAsync(callerId);

        var body = await GetSummaryAsync(callerId);

        Count(body, "trackedSessionCount").Should().Be(4);
        Count(body, "automaticTrackingEntryCount").Should().Be(6, "2 desktop + 1 web-extension + 3 Android rows");
        Count(body, "dayPlanCount").Should().Be(2);
        Count(body, "plannerTaskCount").Should().Be(3);
        Count(body, "dayTemplateCount").Should().Be(1);
        Count(body, "todoListCount").Should().Be(2);
        Count(body, "todoItemCount").Should().Be(5);
        Count(body, "routineCount").Should().Be(1);
        Count(body, "leisureItemCount").Should().Be(9, "1 backlog + 3 project + 5 bucket-list profiles");
        Count(body, "memoryAnchorCount").Should().Be(7);

        // Every seed helper above mints its own activity, so the expected number is whatever the rows say —
        // asserting it against the table still pins that the subquery counts activities and scopes by user.
        var expectedActivities = await db.Set<Activity>().IgnoreQueryFilters()
            .CountAsync(a => a.UserId == callerId, CancellationToken);
        expectedActivities.Should().BeGreaterThan(0, "the seed must actually have created activities");
        Count(body, "activityCount").Should().Be(expectedActivities);

        body.GetProperty("googleCalendarLinked").GetBoolean()
            .Should().BeTrue("the card warns that the calendar link goes with the account");
    }

    // ─── scoping ─────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Half these tables have no query filter behind them — the three <c>Activity*Profile</c> rows carry no
    /// user column at all and are reached through the activity — and the endpoint drops the filters that do
    /// exist. So the hand-written <c>UserId == userId</c> on each subquery is the entire guard, and a
    /// forgotten one would tell the caller they are about to destroy a stranger's data.
    /// </summary>
    [Fact]
    public async Task Summary_CountsOnlyTheCallersRows()
    {
        var callerId = await SeedUserAsync($"deletion-caller-{Guid.NewGuid():N}@test.com");
        var otherId = await SeedUserAsync($"deletion-other-{Guid.NewGuid():N}@test.com");

        await using var db = CreateDbContext();
        await SeedEveryCategoryAsync(db, callerId);
        await SeedEveryCategoryAsync(db, otherId);
        await LinkGoogleCalendarAsync(otherId);

        var body = await GetSummaryAsync(callerId);

        // The exact same numbers as the single-user case: the second account must be invisible, not merged.
        Count(body, "trackedSessionCount").Should().Be(4);
        Count(body, "automaticTrackingEntryCount").Should().Be(6);
        Count(body, "dayPlanCount").Should().Be(2);
        Count(body, "plannerTaskCount").Should().Be(3);
        Count(body, "dayTemplateCount").Should().Be(1);
        Count(body, "todoListCount").Should().Be(2);
        Count(body, "todoItemCount").Should().Be(5);
        Count(body, "routineCount").Should().Be(1);
        Count(body, "leisureItemCount").Should().Be(9);
        Count(body, "memoryAnchorCount").Should().Be(7);

        body.GetProperty("googleCalendarLinked").GetBoolean()
            .Should().BeFalse("only the other account linked a calendar");
    }

    // ─── the partition-window guard ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// <c>WebExtensionActivityEntry</c> carries a global query filter pinning reads to
    /// <c>AppDbContext.CurrentPartitionDate</c> — two years back. Every ordinary read wants that. This one
    /// must not have it: the rows below the cut still exist, deletion still destroys them, and counting
    /// through the filter would under-report by an unbounded margin while looking entirely healthy. The
    /// endpoint's <c>IgnoreQueryFilters</c> is what stops that, and nothing else in the suite would notice
    /// its removal.
    /// </summary>
    [Fact]
    public async Task Summary_CountsWebEntriesOlderThanThePartitionReadWindow()
    {
        var callerId = await SeedUserAsync($"deletion-partition-{Guid.NewGuid():N}@test.com");

        await using var db = CreateDbContext();

        // One inside the read window and one well outside it. The endpoint must see both; a filtered read
        // sees only the first.
        var insideWindow = DateTime.UtcNow.AddDays(-1);
        var beforeWindow = DateTime.UtcNow.AddYears(-5);

        db.Set<WebExtensionActivityEntry>().AddRange(
            NewWebEntry(callerId, insideWindow, "inside.example"),
            NewWebEntry(callerId, beforeWindow, "archived.example"));
        await db.SaveChangesAsync(CancellationToken);

        // Sanity: the filter really is in force on an ordinary read, so a pass below means the endpoint
        // bypassed it rather than the filter having quietly stopped applying.
        var filteredCount = await db.Set<WebExtensionActivityEntry>()
            .CountAsync(e => e.UserId == callerId, CancellationToken);
        filteredCount.Should().Be(1, "the partition filter hides the archived row from a normal read");

        var body = await GetSummaryAsync(callerId);

        Count(body, "automaticTrackingEntryCount").Should().Be(2,
            "deleting the account destroys the archived row too, so the warning has to count it");
    }

    // ─── the span, on the account's own clocks ───────────────────────────────────────────────────────

    /// <summary>
    /// "N months of history" is a count of the user's own calendar days. Read straight off the UTC instants,
    /// a session recorded late in the evening in a far-eastern zone dates to the following day, moving both
    /// ends of the range and the headline figure with them. Kiritimati is +14 with no DST, so the shift is a
    /// constant this test can assert exactly.
    /// </summary>
    [Fact]
    public async Task Summary_ReadsTheSpanOnTheAccountsOwnClocks()
    {
        var kiritimati = TimeZoneInfo.FindSystemTimeZoneById("Pacific/Kiritimati");

        var callerId = await SeedUserAsync($"deletion-zone-{Guid.NewGuid():N}@test.com");
        await using (var setup = (AppDbContext)Fixture.CreateDbContext())
        {
            var user = await setup.Users.IgnoreQueryFilters().FirstAsync(u => u.Id == callerId, CancellationToken);
            user.Timezone = kiritimati;
            await setup.SaveChangesAsync(CancellationToken);
        }

        await using var db = CreateDbContext();
        var activityId = await PlanningTestSeedHelper.SeedActivityAsync(db, $"Zoned {Guid.NewGuid():N}", callerId, CancellationToken);

        // Both instants fall on the day *after* their UTC date in Kiritimati: 2026-03-01 and 2026-03-06
        // local, which is a six-day span. Read as UTC the same rows say 2026-02-28..2026-03-05 — same
        // length, different ends, so the dates are what catch the bug and the span alone would not.
        await SeedHistoryAsync(db, callerId, activityId, new DateTime(2026, 2, 28, 12, 0, 0, DateTimeKind.Utc));
        await SeedHistoryAsync(db, callerId, activityId, new DateTime(2026, 3, 5, 12, 0, 0, DateTimeKind.Utc));

        var body = await GetSummaryAsync(callerId);

        Date(body, "trackedFrom").Should().Be(new DateOnly(2026, 3, 1));
        Date(body, "trackedTo").Should().Be(new DateOnly(2026, 3, 6));
        Count(body, "trackedTimeSpanDays").Should().Be(6, "both ends are counted — 1st through 6th inclusive");
    }

    [Fact]
    public async Task Summary_ForASingleSession_ReportsOneDayNotZero()
    {
        var callerId = await SeedUserAsync($"deletion-oneday-{Guid.NewGuid():N}@test.com");

        await using var db = CreateDbContext();
        var activityId = await PlanningTestSeedHelper.SeedActivityAsync(db, $"OneDay {Guid.NewGuid():N}", callerId, CancellationToken);
        await SeedHistoryAsync(db, callerId, activityId, new DateTime(2026, 5, 4, 8, 0, 0, DateTimeKind.Utc));

        var body = await GetSummaryAsync(callerId);

        Count(body, "trackedSessionCount").Should().Be(1);
        Count(body, "trackedTimeSpanDays").Should().Be(1, "one day of history is one, not zero");
    }

    // ─── helpers ─────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Every purely numeric field the card can render. Enumerated once so the empty-account test cannot
    /// drift out of step with the response as fields are added.
    /// </summary>
    private static readonly string[] CountFields =
    [
        "activityCount", "trackedSessionCount", "automaticTrackingEntryCount", "dayPlanCount",
        "plannerTaskCount", "dayTemplateCount", "todoListCount", "todoItemCount", "routineCount",
        "leisureItemCount", "memoryAnchorCount"
    ];

    /// <summary>
    /// <c>GetProperty</c> rather than a mirrored DTO on purpose: a mirror would deserialize a renamed or
    /// deleted field to 0 and pass, which is precisely the failure these tests exist to catch. This throws.
    /// </summary>
    private static int Count(JsonElement body, string field) => body.GetProperty(field).GetInt32();

    private static DateOnly Date(JsonElement body, string field) => DateOnly.Parse(body.GetProperty(field).GetString()!);

    private async Task<JsonElement> GetSummaryAsync(long callerId)
    {
        await using var factory = CreateFactory(TestRoles.AdminAndUser, callerId);
        var response = await factory.CreateClient().GetAsync(Route, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return await response.Content.ReadFromJsonAsync<JsonElement>(JsonOpts, CancellationToken);
    }

    private async Task<long> SeedUserAsync(string email)
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
        var result = await userManager.CreateAsync(user, Password);
        result.Succeeded.Should().BeTrue();
        return user.Id;
    }

    private async Task LinkGoogleCalendarAsync(long userId)
    {
        await using var db = (AppDbContext)Fixture.CreateDbContext();
        var user = await db.Users.IgnoreQueryFilters().FirstAsync(u => u.Id == userId, CancellationToken);
        user.GoogleCalendarRefreshToken = "refresh-token";
        await db.SaveChangesAsync(CancellationToken);
    }

    /// <summary>
    /// One row in every table the summary claims to count, with a deliberately different number in each —
    /// 4 sessions, 6 tracking rows, 2 day plans, 3 planner tasks, 1 template, 2 lists, 5 items, 1 routine,
    /// 9 leisure profiles, 7 anchors. Shared by the completeness test and the scoping test so the two can
    /// never disagree about what "everything an account owns" means.
    /// </summary>
    private static async Task SeedEveryCategoryAsync(DbContext db, long userId)
    {
        var tag = Guid.NewGuid().ToString("N")[..8];
        var ct = CancellationToken;

        var activityId = await PlanningTestSeedHelper.SeedActivityAsync(db, $"Summary {tag}", userId, ct);

        // 4 sessions, on two distinct days so the span assertions elsewhere have something to disagree with.
        for (var i = 0; i < 4; i++)
            await SeedHistoryAsync(db, userId, activityId, new DateTime(2026, 4, 1, 6 + i, 0, 0, DateTimeKind.Utc));

        // 2 desktop + 1 web + 3 Android = 6.
        for (var i = 0; i < 2; i++)
            db.Set<DesktopActivityEntry>().Add(NewDesktopEntry(userId, DateTime.UtcNow.AddMinutes(-i), $"proc{i}.exe"));
        db.Set<WebExtensionActivityEntry>().Add(NewWebEntry(userId, DateTime.UtcNow, $"{tag}.example"));
        for (var i = 0; i < 3; i++)
            db.Set<AndroidSessionData>().Add(new AndroidSessionData
            {
                UserId = userId,
                DeviceId = $"device-{tag}",
                PackageName = $"com.example.app{i}",
                AppLabel = $"App {i}",
                SessionStartUtc = DateTime.UtcNow.AddMinutes(-i),
                SessionEndUtc = DateTime.UtcNow,
                DurationSeconds = 60
            });
        await db.SaveChangesAsync(ct);

        // 2 day plans carrying 3 planner tasks between them, and 1 template.
        var firstDay = await PlanningTestSeedHelper.SeedCalendarAsync(db, new DateOnly(2026, 4, 1), userId, ct);
        var secondDay = await PlanningTestSeedHelper.SeedCalendarAsync(db, new DateOnly(2026, 4, 2), userId, ct);
        await PlanningTestSeedHelper.SeedPlannerTaskAsync(db, activityId, firstDay, new TimeOnly(9, 0), new TimeOnly(10, 0), userId, ct: ct);
        await PlanningTestSeedHelper.SeedPlannerTaskAsync(db, activityId, firstDay, new TimeOnly(11, 0), new TimeOnly(12, 0), userId, ct: ct);
        await PlanningTestSeedHelper.SeedPlannerTaskAsync(db, activityId, secondDay, new TimeOnly(9, 0), new TimeOnly(10, 0), userId, ct: ct);
        await PlanningTestSeedHelper.SeedTaskPlannerDayTemplateAsync(db, userId, $"Template {tag}", ct);

        // 2 lists holding 5 items, plus 1 routine.
        var priorityId = await TodoListTestSeedHelper.SeedTaskPriorityAsync(db, 1, userId, $"Priority {tag}", ct);
        var firstList = await TodoListTestSeedHelper.SeedTodoListAsync(db, userId, $"List A {tag}", ct: ct);
        var secondList = await TodoListTestSeedHelper.SeedTodoListAsync(db, userId, $"List B {tag}", ct: ct);
        // A distinct activity per item: todo_list_item is unique on (user, activity, list), so five items
        // sharing one activity would fail on the index rather than on an assertion.
        for (var i = 0; i < 3; i++)
        {
            var itemActivityId = await SeedActivityWithRoleAsync(db, userId, $"Item {tag}-a{i}");
            await TodoListTestSeedHelper.SeedTodoListItemAsync(db, itemActivityId, priorityId, firstList, userId, ct: ct);
        }

        for (var i = 0; i < 2; i++)
        {
            var itemActivityId = await SeedActivityWithRoleAsync(db, userId, $"Item {tag}-b{i}");
            await TodoListTestSeedHelper.SeedTodoListItemAsync(db, itemActivityId, priorityId, secondList, userId, ct: ct);
        }

        var timePeriodId = await TodoListTestSeedHelper.SeedRoutineTimePeriodAsync(db, userId, $"Period {tag}", ct: ct);
        await TodoListTestSeedHelper.SeedRoutineTodoListAsync(db, activityId, timePeriodId, userId, ct: ct);

        // 1 + 3 + 5 = 9 leisure profiles, each on its own activity (the FK is one-to-one).
        await SeedBacklogProfileAsync(db, userId, $"Backlog {tag}");
        for (var i = 0; i < 3; i++)
            await SeedProjectProfileAsync(db, userId, $"Project {tag}-{i}");
        for (var i = 0; i < 5; i++)
            await SeedBucketListProfileAsync(db, userId, $"Bucket {tag}-{i}");

        // 7 anchors, spread across months so the (activity, year, month) index has nothing to trip on.
        for (var i = 0; i < 7; i++)
            db.Set<MemoryAnchor>().Add(new MemoryAnchor
            {
                UserId = userId,
                ActivityId = activityId,
                AnchorMonth = i + 1,
                AnchorYear = 2025,
                HighlightNote = $"Anchor {i}",
                Rating = 5
            });
        await db.SaveChangesAsync(ct);
    }

    private static async Task SeedHistoryAsync(DbContext db, long userId, long activityId, DateTime startUtc)
    {
        db.Set<ActivityHistory>().Add(new ActivityHistory
        {
            UserId = userId,
            ActivityId = activityId,
            StartTimestamp = startUtc,
            EndTimestamp = startUtc.AddMinutes(30),
            Length = new IntTime(0, 30)
        });
        await db.SaveChangesAsync(CancellationToken);
    }

    private static WebExtensionActivityEntry NewWebEntry(long userId, DateTime windowStart, string domain) => new()
    {
        UserId = userId,
        WindowStart = windowStart,
        Domain = domain,
        Url = null,
        ActiveSeconds = 60,
        BackgroundSeconds = 0,
        IsFinal = true
    };

    private static DesktopActivityEntry NewDesktopEntry(long userId, DateTime windowStart, string processName) => new()
    {
        UserId = userId,
        WindowStart = windowStart,
        ProcessName = processName,
        ProductName = "Summary",
        WindowTitle = processName,
        ExecutablePath = $"/usr/bin/{processName}",
        IsFullscreen = false,
        ActiveSeconds = 60,
        BackgroundSeconds = 0,
        IsPlayingSound = false,
        ActiveMonitor = 0
    };

    private static async Task<long> SeedActivityWithRoleAsync(DbContext db, long userId, string name)
    {
        var role = new ActivityRole { UserId = userId, Name = $"{name} role", Color = "#112233", Icon = "fas fa-star" };
        db.Set<ActivityRole>().Add(role);
        await db.SaveChangesAsync(CancellationToken);

        var activity = new Activity { UserId = userId, Name = name, RoleId = role.Id };
        db.Set<Activity>().Add(activity);
        await db.SaveChangesAsync(CancellationToken);
        return activity.Id;
    }

    private static async Task<long> SeedLookupAsync<TLookup>(DbContext db, long userId, string text)
        where TLookup : BaseLookupWithUser, new()
    {
        var lookup = new TLookup { UserId = userId, Text = text, SortOrder = 0 };
        db.Set<TLookup>().Add(lookup);
        await db.SaveChangesAsync(CancellationToken);
        return lookup.Id;
    }

    private static async Task SeedBacklogProfileAsync(DbContext db, long userId, string name)
    {
        var activityId = await SeedActivityWithRoleAsync(db, userId, name);
        db.Set<ActivityBacklogProfile>().Add(new ActivityBacklogProfile
        {
            ActivityId = activityId,
            LocationTypeId = await SeedLookupAsync<ActivityLocationType>(db, userId, $"{name} location"),
            WeatherDependencyId = await SeedLookupAsync<ActivityWeatherDependency>(db, userId, $"{name} weather"),
            ExpectedCostTierId = await SeedLookupAsync<ActivityExpectedCostTier>(db, userId, $"{name} cost"),
            EnergyLevel = EnergyLevel.Low,
            MinParticipants = 1,
            DurationMinutes = 30,
            IsRepeatable = true
        });
        await db.SaveChangesAsync(CancellationToken);
    }

    private static async Task SeedProjectProfileAsync(DbContext db, long userId, string name)
    {
        var activityId = await SeedActivityWithRoleAsync(db, userId, name);
        db.Set<ActivityProjectProfile>().Add(new ActivityProjectProfile
        {
            ActivityId = activityId,
            DifficultyLevel = DifficultyLevel.Beginner,
            ProjectArea = "Garage",
            EstimatedHours = 4,
            ReadinessStatus = ReadinessStatus.ReadyToStart
        });
        await db.SaveChangesAsync(CancellationToken);
    }

    private static async Task SeedBucketListProfileAsync(DbContext db, long userId, string name)
    {
        var activityId = await SeedActivityWithRoleAsync(db, userId, name);
        db.Set<ActivityBucketListProfile>().Add(new ActivityBucketListProfile
        {
            ActivityId = activityId,
            ExperienceTypeId = await SeedLookupAsync<ActivityExperienceType>(db, userId, $"{name} experience"),
            ComfortZoneStep = 2,
            RequiresTravel = false,
            InspirationSource = "A friend's story"
        });
        await db.SaveChangesAsync(CancellationToken);
    }
}
