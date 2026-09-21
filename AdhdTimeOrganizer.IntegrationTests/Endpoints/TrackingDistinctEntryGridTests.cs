using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AdhdTimeOrganizer.Core.domain.model.entity.activity;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using AdhdTimeOrganizer.IntegrationTests.Reminders;
using AdhdTimeOrganizer.Tracking.domain.model.entity.activityTracking;
using AdhdTimeOrganizer.Tracking.domain.model.entity.activityTracking.android;
using AdhdTimeOrganizer.Tracking.domain.model.entity.activityTracking.desktop;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.domain.@enum;
using Sydowwe.Framework.Testing;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Endpoints;

/// <summary>
/// TEST-9 -- the four hand-written read endpoints under the two <c>activity-tracking</c> settings groups.
/// Before this file they were covered by <see cref="TrackingRouteSmokeTests.TrackingGridRoutes_AreRegistered"/>
/// and <see cref="TrackingRouteSmokeTests.DesktopDistinctProcessesRoute_IsRegistered"/> alone -- a route
/// ping with <c>useFilter: false</c>, which never reaches any of their filtering, grouping or paging code
/// -- and <c>fetch-categories-and-roles-by-pattern</c> had no test at all.
/// <para>
/// The four are hand-written <c>Endpoint&lt;,&gt;</c> classes rather than <c>BaseGridEndpoint</c>
/// derivatives, so each scopes to the caller with its own <c>query.FilteredByUser(User.GetId())</c> line
/// over an entity that is <em>also</em> covered by the global <c>IEntityWithUser</c> query filter. That
/// redundancy is the point: the line looks deletable, deleting it changes nothing observable while the
/// global filter holds, and the two would then have to fail together to leak -- which is exactly the
/// arrangement <c>WebExtensionActivityEntry</c> shows can come apart (see
/// <see cref="TrackingRouteSmokeTests.WebExtensionActivityEntry_KeepsBothOfItsQueryFilters"/>). The
/// scoping cases here are cheap and pin the outcome rather than the mechanism.
/// </para>
/// <para>
/// Not repeated here, because they already exist: the page-size bounds these two grids gained with
/// <c>CQ-14</c> (<c>TrackingValidationBoundsTests.DistinctEntriesGrid_RejectsAPageSizeOutsideItsBounds</c>)
/// and the <c>Regex</c>-means-regex semantics they gained with <c>CQ-12</c>
/// (<see cref="TrackingPatternMatchSemanticsTests"/>). The task prompt predates both fixes and describes
/// them as gaps to pin under <c>[Trait("Status", "KnownGap")]</c>; they are fixed, so there is no gap and
/// nothing here carries that trait.
/// </para>
/// <para>
/// <c>PERF-4</c>: neither <c>grid</c> query has a date bound, so every row the caller owns is in scope
/// regardless of when it was recorded. Cases that assert a <c>itemsCount</c> narrow to their own rows with
/// an exact-match filter on a GUID-tokened process/package name rather than trusting a total.
/// </para>
/// </summary>
file static class DistinctSeed
{
    public const string DesktopGridUrl = "/api/activity-tracking/desktop/grid";
    public const string AndroidGridUrl = "/api/activity-tracking/android/grid";
    public const string DistinctProcessesUrl = "/api/activity-tracking/desktop/distinct-processes";
    public const string CategoriesAndRolesUrl = "/api/activity-tracking/desktop/fetch-categories-and-roles-by-pattern";

    /// <summary>
    /// No string-enum converter, matching <see cref="TrackingPatternMatchSemanticsTests"/> -- the match
    /// types below go on the wire as their integer values.
    /// </summary>
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Taken from the enum rather than written out: the declared order is
    /// <c>Exact, Wildcard, Contains, Regex</c>, so <c>Contains</c> is <b>2</b>, not the 1 an alphabetical
    /// reading suggests.
    /// </summary>
    public const int ExactMatch = (int)PatternMatchType.Exact;

    public const int ContainsMatch = (int)PatternMatchType.Contains;

    /// <summary>
    /// Inside the 2026 partition of the two range-partitioned ledgers, and <c>DateTimeKind.Utc</c>: an
    /// <c>Unspecified</c> instant is silently accepted under the tests' legacy-timestamp switch and throws
    /// in production (<c>CQ-1</c>).
    /// </summary>
    private static readonly DateTime BaseInstant = new(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc);

    public static string Token() => Guid.NewGuid().ToString("N")[..8];

    /// <summary>
    /// One desktop ledger row. <paramref name="minuteOffset"/> keeps rows that deliberately share
    /// <c>(ProductName, ProcessName, WindowTitle)</c> clear of the
    /// <c>(user_id, window_start, record_date, process_name, window_title)</c> unique index, and saving one
    /// row at a time makes the returned ids ascending in seed order -- which the "lowest id" assertions read.
    /// </summary>
    public static async Task<long> DesktopEntryAsync(
        DbContext db, long userId, string processName, string productName, string windowTitle, int minuteOffset)
    {
        var entry = new DesktopActivityEntry
        {
            UserId = userId,
            WindowStart = BaseInstant.AddMinutes(minuteOffset),
            ProcessName = processName,
            ProductName = productName,
            WindowTitle = windowTitle,
            ExecutablePath = $"/usr/bin/{processName}",
            IsFullscreen = false,
            ActiveSeconds = 60,
            BackgroundSeconds = 0,
            IsPlayingSound = false,
            ActiveMonitor = 0
        };

        db.Set<DesktopActivityEntry>().Add(entry);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return entry.Id;
    }

    /// <summary>
    /// One android session. <paramref name="minuteOffset"/> keeps repeated
    /// <c>(PackageName, AppLabel)</c> pairs clear of the
    /// <c>(user_id, device_id, package_name, session_start_utc)</c> unique index.
    /// </summary>
    public static async Task<long> AndroidSessionAsync(
        DbContext db, long userId, string packageName, string appLabel, int minuteOffset)
    {
        var start = BaseInstant.AddMinutes(minuteOffset);
        var session = new AndroidSessionData
        {
            UserId = userId,
            DeviceId = "test-device",
            PackageName = packageName,
            AppLabel = appLabel,
            SessionStartUtc = start,
            SessionEndUtc = start.AddSeconds(300),
            DurationSeconds = 300
        };

        db.Set<AndroidSessionData>().Add(session);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return session.Id;
    }

    public static async Task<long> CategoryAsync(DbContext db, long userId, string name)
    {
        var category = new ActivityCategory { UserId = userId, Name = name, Color = "#112233" };
        db.Set<ActivityCategory>().Add(category);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return category.Id;
    }

    public static async Task<long> RoleAsync(DbContext db, long userId, string name)
    {
        var role = new ActivityRole { UserId = userId, Name = name, Color = "#445566" };
        db.Set<ActivityRole>().Add(role);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return role.Id;
    }

    /// <summary>
    /// A mapping whose target is the role/category group -- <c>RoleId</c> and <c>CategoryId</c> together are
    /// one group under <c>CK_TrackerDesktopMappingByPattern_TargetRequired</c>, so <c>IsIgnored</c> stays
    /// null. Each row needs its own <c>ProcessName</c>: the per-user pattern index is
    /// <c>NULLS NOT DISTINCT</c>, so two rows leaving all three pattern columns equal collide.
    /// </summary>
    public static async Task<long> MappingAsync(
        DbContext db, long userId, long categoryId, long roleId, string processName)
    {
        var mapping = new TrackerDesktopMappingByPattern
        {
            UserId = userId,
            ProcessName = processName,
            ProcessNameMatchType = PatternMatchType.Exact,
            IsActive = true,
            CategoryId = categoryId,
            RoleId = roleId
        };

        db.Set<TrackerDesktopMappingByPattern>().Add(mapping);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return mapping.Id;
    }

    /// <summary>
    /// A desktop rule with any target shape, for the B24 coverage cases. The pattern columns are the
    /// caller's — a window-title constraint is what makes one rule more specific than another.
    /// </summary>
    public static async Task<long> DesktopRuleAsync(
        DbContext db, long userId,
        string? processName = null, PatternMatchType processMatch = PatternMatchType.Exact,
        string? windowTitle = null, PatternMatchType windowTitleMatch = PatternMatchType.Exact,
        long? activityId = null, long? roleId = null, long? categoryId = null, bool ignored = false,
        bool isActive = true)
    {
        var mapping = new TrackerDesktopMappingByPattern
        {
            UserId = userId,
            ProcessName = processName,
            ProcessNameMatchType = processName == null ? null : processMatch,
            WindowTitle = windowTitle,
            WindowTitleMatchType = windowTitle == null ? null : windowTitleMatch,
            IsActive = isActive,
            IsIgnored = ignored ? true : null,
            ActivityId = activityId,
            RoleId = roleId,
            CategoryId = categoryId
        };

        db.Set<TrackerDesktopMappingByPattern>().Add(mapping);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return mapping.Id;
    }

    /// <summary>The android twin of <see cref="DesktopRuleAsync"/>.</summary>
    public static async Task<long> AndroidRuleAsync(
        DbContext db, long userId,
        string? packageName = null, PatternMatchType packageMatch = PatternMatchType.Exact,
        string? appLabel = null, PatternMatchType appLabelMatch = PatternMatchType.Exact,
        long? activityId = null, bool ignored = false, bool isActive = true)
    {
        var mapping = new TrackerAndroidMappingByPattern
        {
            UserId = userId,
            PackageName = packageName,
            PackageNameMatchType = packageName == null ? null : packageMatch,
            AppLabel = appLabel,
            AppLabelMatchType = appLabel == null ? null : appLabelMatch,
            IsActive = isActive,
            IsIgnored = ignored ? true : null,
            ActivityId = activityId
        };

        db.Set<TrackerAndroidMappingByPattern>().Add(mapping);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return mapping.Id;
    }

    /// <summary>
    /// Reads a row's <c>coveredBy</c> as the client will: null when the tuple is undecided, otherwise the
    /// rule id, the target group name (string enum, no naming policy) and the chip text.
    /// </summary>
    public static (long MappingId, string Target, string? TargetName)? CoveredBy(JsonElement row)
    {
        var covered = row.GetProperty("coveredBy");
        if (covered.ValueKind == JsonValueKind.Null)
            return null;

        return (
            covered.GetProperty("mappingId").GetInt64(),
            covered.GetProperty("target").GetString()!,
            covered.GetProperty("targetName").ValueKind == JsonValueKind.Null ? null : covered.GetProperty("targetName").GetString());
    }

    /// <summary>
    /// <c>filter: null</c>, not <c>{}</c> -- <c>BaseFilterRequest.Filter</c> is <c>required TFilter?</c>, so
    /// an explicit null satisfies the required-presence check without deserializing a filter at all. See
    /// <c>TrackingRouteSmokeTests.GridBody</c>.
    /// </summary>
    public static object UnfilteredBody(int itemsPerPage = 200) => new
    {
        useFilter = false,
        filter = (object?)null,
        sortBy = Array.Empty<object>(),
        itemsPerPage,
        page = 1
    };

    public static object FilteredBody(object filter, int itemsPerPage = 50, int page = 1) => new
    {
        useFilter = true,
        filter,
        sortBy = Array.Empty<object>(),
        itemsPerPage,
        page
    };
}

// =====================================================================================================
// POST /api/activity-tracking/desktop/grid
// =====================================================================================================

[Collection("Postgres")]
public class DesktopDistinctEntryGridTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    private const long UserId = FakeLoggedUserService.TestUserId;

    private record GridResponse(JsonElement[] Items, int ItemsCount, int PageCount);

    /// <summary>
    /// The endpoint's own <c>FilteredByUser</c> line and the global <c>IEntityWithUser</c> filter both have
    /// to hold for this to pass, and neither is visible in the response -- a leak here is a well-formed 200
    /// containing another user's window titles, which is the most sensitive column in the ledger.
    /// </summary>
    [Fact]
    public async Task Grid_ExcludesOtherUsersEntries()
    {
        var token = DistinctSeed.Token();

        await using (var db = CreateDbContext())
        {
            var other = await ReminderSeedHelper.EnsureOtherUserAsync(db, CancellationToken);

            await DistinctSeed.DesktopEntryAsync(db, UserId, $"mine-{token}.exe", "Mine", "my window", 0);
            await DistinctSeed.DesktopEntryAsync(db, other.Id, $"theirs-{token}.exe", "Theirs", "their window", 1);
        }

        var body = await PostAsync(DistinctSeed.UnfilteredBody());
        var processNames = ProcessNames(body);

        processNames.Should().Contain($"mine-{token}.exe");
        processNames.Should().NotContain($"theirs-{token}.exe",
            "another user's desktop ledger must never reach this grid");
        body.Items.Select(i => i.GetProperty("windowTitle").GetString())
            .Should().NotContain("their window");
    }

    /// <summary>
    /// The grid groups by <c>(ProductName, ProcessName, WindowTitle)</c> and projects
    /// <c>Id = g.Min(e =&gt; e.Id)</c>. The client keys rows on that id, so it has to be the lowest
    /// underlying id and not merely <em>some</em> id from the group: a switch to <c>Max</c> or <c>First</c>
    /// would still return one row per distinct window and still look right on screen, while every row key
    /// changed the moment the user recorded another window of the same title.
    /// </summary>
    [Fact]
    public async Task Grid_CollapsesRepeatedEntriesIntoOneRow()
    {
        var token = DistinctSeed.Token();
        var processName = $"collapse-{token}.exe";
        var ids = new List<long>();

        await using (var db = CreateDbContext())
        {
            for (var i = 0; i < 5; i++)
                ids.Add(await DistinctSeed.DesktopEntryAsync(db, UserId, processName, "Collapse App", "shared window", i));

            // Differs from the five only in WindowTitle, which is part of the grouping key.
            await DistinctSeed.DesktopEntryAsync(db, UserId, processName, "Collapse App", "other window", 5);
        }

        var body = await PostAsync(DistinctSeed.FilteredBody(
            new { processName, processNameMatchType = DistinctSeed.ExactMatch }));

        body.ItemsCount.Should().Be(2, "six rows across two distinct window titles are two grid rows");
        body.Items.Should().HaveCount(2);

        var collapsed = body.Items.Single(i => i.GetProperty("windowTitle").GetString() == "shared window");
        collapsed.GetProperty("id").GetInt64().Should().Be(ids.Min(),
            "the row key is the lowest id in the group, so it stays stable as more windows are recorded");
        collapsed.GetProperty("productName").GetString().Should().Be("Collapse App");
    }

    /// <summary>
    /// Paging is applied to the grouped projection, not to the ledger rows behind it, and
    /// <c>PageCount</c> is computed from the <em>distinct</em> count -- so a regression that counted rows
    /// instead of groups would report a page count larger than the number of pages that actually exist.
    /// Ordering is the <c>SortByMany</c> default (ascending <c>Id</c>) since <c>sortBy</c> is empty, which
    /// is what makes the middle page assertable at all.
    /// </summary>
    [Fact]
    public async Task Grid_PaginatesAndReportsPageCount()
    {
        var token = DistinctSeed.Token();
        var processName = $"paged-{token}.exe";
        var ids = new List<long>();

        await using (var db = CreateDbContext())
            for (var i = 0; i < 7; i++)
                ids.Add(await DistinctSeed.DesktopEntryAsync(db, UserId, processName, "Paged App", $"window {i}", i));

        var body = await PostAsync(DistinctSeed.FilteredBody(
            new { processName, processNameMatchType = DistinctSeed.ExactMatch },
            itemsPerPage: 3,
            page: 2));

        body.ItemsCount.Should().Be(7, "the count is of distinct entries, not of the ledger rows behind them");
        body.PageCount.Should().Be(3, "seven entries at three per page is three pages, the last one short");
        body.Items.Should().HaveCount(3);
        body.Items.Select(i => i.GetProperty("id").GetInt64())
            .Should().Equal(ids.Order().Skip(3).Take(3),
                "page 2 is the second window of three in ascending id order");
    }

    /// <summary>
    /// <c>useFilter: true</c> with a real filter object -- the branch the route smoke test never reaches.
    /// Both arms matter: <c>Exact</c> is the fall-through arm of <c>ApplyStringMatchFilter</c>'s switch, so
    /// an unhandled match type silently behaves like it, and <c>Contains</c> is the one the settings screen
    /// actually sends.
    /// </summary>
    [Fact]
    public async Task Grid_FiltersByExactAndContains()
    {
        var token = DistinctSeed.Token();
        var exactName = $"vscode-{token}.exe";
        var siblingName = $"vscode-plus-{token}.exe";
        var otherName = $"firefox-{token}.exe";

        await using (var db = CreateDbContext())
        {
            await DistinctSeed.DesktopEntryAsync(db, UserId, exactName, "Code", "a window", 0);
            await DistinctSeed.DesktopEntryAsync(db, UserId, siblingName, "Code Plus", "a window", 1);
            await DistinctSeed.DesktopEntryAsync(db, UserId, otherName, "Firefox", "a window", 2);
        }

        var exact = await PostAsync(DistinctSeed.FilteredBody(
            new { processName = exactName, processNameMatchType = DistinctSeed.ExactMatch }));

        ProcessNames(exact).Should().ContainSingle().Which.Should().Be(exactName,
            "an exact match must not also catch the process name it is a prefix of");

        var contains = await PostAsync(DistinctSeed.FilteredBody(
            new { processName = "vscode-", processNameMatchType = DistinctSeed.ContainsMatch }));

        var containsNames = ProcessNames(contains);
        containsNames.Should().Contain([exactName, siblingName]);
        containsNames.Should().NotContain(otherName);
    }

    // ---- B24: coveredBy + coverage ----------------------------------------------------------------

    /// <summary>
    /// B24 — a row names the active rule that matches it, with the rule id (the deep link), the target
    /// group and the activity's name (the chip text); a row no rule matches carries an explicit null. The
    /// ledger keeps listing a mapped tuple by design (§103), so this field is the only thing on the screen
    /// that says which rows still need a decision.
    /// </summary>
    [Fact(DisplayName = "B24: desktop/grid says which active rule covers each tuple, null when none")]
    public async Task Grid_CoveredBy_NamesTheRuleTheIngestWouldPick()
    {
        var token = DistinctSeed.Token();
        var mapped = $"vlc-{token}.exe";
        var undecided = $"undecided-{token}.exe";
        long activityId, mappingId;

        await using (var db = CreateDbContext())
        {
            activityId = await ActivityProfileTestSeedHelper.SeedActivityAsync(db, UserId, $"Read a Novel {token}");
            mappingId = await DistinctSeed.DesktopRuleAsync(db, UserId, processName: mapped, activityId: activityId);

            await DistinctSeed.DesktopEntryAsync(db, UserId, mapped, "VLC", "Documentary.mkv", 0);
            await DistinctSeed.DesktopEntryAsync(db, UserId, undecided, "Nothing", "no rule", 1);
        }

        var body = await PostAsync(DistinctSeed.FilteredBody(
            new { processName = $"-{token}.exe", processNameMatchType = DistinctSeed.ContainsMatch }));

        var mappedRow = body.Items.Single(i => i.GetProperty("processName").GetString() == mapped);
        DistinctSeed.CoveredBy(mappedRow).Should().Be((mappingId, "Activity", $"Read a Novel {token}"),
            "the chip needs the rule id to link to, the target group, and the activity name to render");

        var undecidedRow = body.Items.Single(i => i.GetProperty("processName").GetString() == undecided);
        DistinctSeed.CoveredBy(undecidedRow).Should().BeNull("no active rule matches this tuple");
    }

    /// <summary>
    /// B24 rule 1 — the grid answers with the rule the <em>ingest</em> would pick, which means the
    /// heartbeat's specificity order (CQ-16): a rule constraining process <em>and</em> window title beats a
    /// process-only rule that also matches. Both sides resolve through <c>DesktopMappingRuleSet</c>; this is
    /// the pin that they keep doing so, because a grid re-implementing "first match" over an unordered read
    /// would disagree with attribution and still return a well-formed 200.
    /// </summary>
    [Fact(DisplayName = "B24: coveredBy is the most specific matching rule, as the heartbeat resolves it")]
    public async Task Grid_CoveredBy_PrefersTheMoreSpecificRule_LikeTheIngest()
    {
        var token = DistinctSeed.Token();
        var processName = $"chrome-{token}.exe";
        long broadRuleId, narrowRuleId;

        await using (var db = CreateDbContext())
        {
            var broad = await ActivityProfileTestSeedHelper.SeedActivityAsync(db, UserId, $"Browsing {token}");
            var narrow = await ActivityProfileTestSeedHelper.SeedActivityAsync(db, UserId, $"Code review {token}");

            // The broad rule is older (lower id) — id order alone would pick it; specificity must win first.
            broadRuleId = await DistinctSeed.DesktopRuleAsync(db, UserId,
                processName: processName, activityId: broad);
            narrowRuleId = await DistinctSeed.DesktopRuleAsync(db, UserId,
                processName: processName, windowTitle: "GitHub", windowTitleMatch: PatternMatchType.Contains, activityId: narrow);

            await DistinctSeed.DesktopEntryAsync(db, UserId, processName, "Chrome", "Pull request · GitHub", 0);
            await DistinctSeed.DesktopEntryAsync(db, UserId, processName, "Chrome", "YouTube", 1);
        }

        var body = await PostAsync(DistinctSeed.FilteredBody(
            new { processName, processNameMatchType = DistinctSeed.ExactMatch }));

        var github = body.Items.Single(i => i.GetProperty("windowTitle").GetString() == "Pull request · GitHub");
        DistinctSeed.CoveredBy(github)!.Value.MappingId.Should().Be(narrowRuleId,
            "two columns constrained beats one, regardless of which rule is older");

        var youtube = body.Items.Single(i => i.GetProperty("windowTitle").GetString() == "YouTube");
        DistinctSeed.CoveredBy(youtube)!.Value.MappingId.Should().Be(broadRuleId,
            "the narrow rule does not match this title, so the process-only rule is the ingest's answer");
    }

    /// <summary>
    /// B24 rules 2 and 3 side by side. An inactive rule matches nothing at ingest, so a tuple only it
    /// matches is <em>uncovered</em> — toggling a rule off makes its tuples reappear as undecided. An
    /// ignore rule does not drop rows already in the ledger, and those read "ignored by rule N", not
    /// undecided; <c>targetName</c> is null because there is nothing to name.
    /// </summary>
    [Fact(DisplayName = "B24: an inactive rule does not cover; an ignore rule does, with no target name")]
    public async Task Grid_CoveredBy_SkipsInactiveRules_AndReportsIgnoreRules()
    {
        var token = DistinctSeed.Token();
        var inactiveProcess = $"inactive-{token}.exe";
        var ignoredProcess = $"ignored-{token}.exe";
        long ignoreRuleId;

        await using (var db = CreateDbContext())
        {
            var activityId = await ActivityProfileTestSeedHelper.SeedActivityAsync(db, UserId, $"Switched off {token}");
            await DistinctSeed.DesktopRuleAsync(db, UserId, processName: inactiveProcess, activityId: activityId, isActive: false);
            ignoreRuleId = await DistinctSeed.DesktopRuleAsync(db, UserId, processName: ignoredProcess, ignored: true);

            await DistinctSeed.DesktopEntryAsync(db, UserId, inactiveProcess, "Off", "a window", 0);
            await DistinctSeed.DesktopEntryAsync(db, UserId, ignoredProcess, "Ignored", "a window", 1);
        }

        var body = await PostAsync(DistinctSeed.FilteredBody(
            new { processName = $"-{token}.exe", processNameMatchType = DistinctSeed.ContainsMatch }));

        var inactiveRow = body.Items.Single(i => i.GetProperty("processName").GetString() == inactiveProcess);
        DistinctSeed.CoveredBy(inactiveRow).Should().BeNull("a rule with isActive: false matches nothing at ingest");

        var ignoredRow = body.Items.Single(i => i.GetProperty("processName").GetString() == ignoredProcess);
        DistinctSeed.CoveredBy(ignoredRow).Should().Be((ignoreRuleId, "Ignored", (string?)null));
    }

    /// <summary>
    /// The third target group. The chip text for a role/category rule is the two names joined with
    /// <c>·</c> when both are set — the client has nothing else to render it from without a second request
    /// per row.
    /// </summary>
    [Fact(DisplayName = "B24: a role+category rule reports RoleOrCategory with both names joined")]
    public async Task Grid_CoveredBy_JoinsRoleAndCategoryNames()
    {
        var token = DistinctSeed.Token();
        var processName = $"slack-{token}.exe";
        long ruleId;

        await using (var db = CreateDbContext())
        {
            var roleId = await DistinctSeed.RoleAsync(db, UserId, $"Work {token}");
            var categoryId = await DistinctSeed.CategoryAsync(db, UserId, $"Chat {token}");
            ruleId = await DistinctSeed.DesktopRuleAsync(db, UserId, processName: processName, roleId: roleId, categoryId: categoryId);

            await DistinctSeed.DesktopEntryAsync(db, UserId, processName, "Slack", "general", 0);
        }

        var body = await PostAsync(DistinctSeed.FilteredBody(
            new { processName, processNameMatchType = DistinctSeed.ExactMatch }));

        DistinctSeed.CoveredBy(body.Items.Single())
            .Should().Be((ruleId, "RoleOrCategory", $"Work {token} · Chat {token}"));
    }

    /// <summary>
    /// The <c>coverage</c> filter is resolved in memory over the whole distinct set (patterns have no SQL
    /// translation that agrees with the ingest), so its <c>itemsCount</c> and paging have to be exact
    /// anyway — "12 still to decide" is a header the client prints from it. Sent as the client's camelCase
    /// wire name and the string enum member, so this cannot pass by agreeing with the server's spelling.
    /// </summary>
    [Fact(DisplayName = "B24: coverage=Uncovered / Covered narrow the grid and count exactly")]
    public async Task Grid_CoverageFilter_NarrowsAndCountsExactly()
    {
        var token = DistinctSeed.Token();
        var processName = $"coverage-{token}.exe";

        await using (var db = CreateDbContext())
        {
            var activityId = await ActivityProfileTestSeedHelper.SeedActivityAsync(db, UserId, $"Decided {token}");
            await DistinctSeed.DesktopRuleAsync(db, UserId,
                processName: processName, windowTitle: "decided", windowTitleMatch: PatternMatchType.Contains, activityId: activityId);

            await DistinctSeed.DesktopEntryAsync(db, UserId, processName, "Coverage", "decided one", 0);
            await DistinctSeed.DesktopEntryAsync(db, UserId, processName, "Coverage", "decided two", 1);
            for (var i = 0; i < 5; i++)
                await DistinctSeed.DesktopEntryAsync(db, UserId, processName, "Coverage", $"open {i}", 2 + i);
        }

        var all = await PostAsync(DistinctSeed.FilteredBody(
            new { processName, processNameMatchType = DistinctSeed.ExactMatch }));
        all.ItemsCount.Should().Be(7, "no coverage value is no opinion — the grid stays the raw observation record");

        var uncovered = await PostAsync(DistinctSeed.FilteredBody(
            new { processName, processNameMatchType = DistinctSeed.ExactMatch, coverage = "Uncovered" },
            itemsPerPage: 2, page: 2));
        uncovered.ItemsCount.Should().Be(5, "five tuples have no matching rule");
        uncovered.PageCount.Should().Be(3, "five at two per page");
        uncovered.Items.Should().HaveCount(2, "page 2 of 3 is full");
        uncovered.Items.Select(i => i.GetProperty("windowTitle").GetString())
            .Should().OnlyContain(t => t!.StartsWith("open "));
        uncovered.Items.Select(DistinctSeed.CoveredBy).Should().OnlyContain(c => c == null);

        var covered = await PostAsync(DistinctSeed.FilteredBody(
            new { processName, processNameMatchType = DistinctSeed.ExactMatch, coverage = "Covered" }));
        covered.ItemsCount.Should().Be(2, "two tuples match the rule");
        covered.Items.Select(i => i.GetProperty("windowTitle").GetString())
            .Should().BeEquivalentTo("decided one", "decided two");
        covered.Items.Select(DistinctSeed.CoveredBy).Should().OnlyContain(c => c != null);

        var explicitAll = await PostAsync(DistinctSeed.FilteredBody(
            new { processName, processNameMatchType = DistinctSeed.ExactMatch, coverage = "All" }));
        explicitAll.ItemsCount.Should().Be(7);
    }

    /// <summary>
    /// An undefined coverage value is a 400 from the validator's <c>IsInEnum</c>, not a silent fall-through
    /// to <c>All</c> — a client sending garbage would otherwise get the full grid with a 200, which is the
    /// exact shape of failure B11 was written about. Sent as a number: the string converter still accepts
    /// integers, so an out-of-range one binds and reaches the validator rather than dying in the binder.
    /// </summary>
    [Fact(DisplayName = "B24: an undefined coverage value is a 400")]
    public async Task Grid_CoverageFilter_RejectsAnUndefinedValue()
    {
        var response = await CreateClient().PostAsJsonAsync(DistinctSeed.DesktopGridUrl,
            DistinctSeed.FilteredBody(new { coverage = 99 }), DistinctSeed.Json, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Grid_Unauthenticated_Returns401()
    {
        var response = await CreateUnauthenticatedClient().PostAsJsonAsync(
            DistinctSeed.DesktopGridUrl, DistinctSeed.UnfilteredBody(), DistinctSeed.Json, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ---- helpers ---------------------------------------------------------

    private static List<string> ProcessNames(GridResponse body) =>
        body.Items.Select(i => i.GetProperty("processName").GetString()!).ToList();

    private async Task<GridResponse> PostAsync(object requestBody)
    {
        var response = await CreateClient().PostAsJsonAsync(
            DistinctSeed.DesktopGridUrl, requestBody, DistinctSeed.Json, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<GridResponse>(DistinctSeed.Json, CancellationToken))!;
    }
}

// =====================================================================================================
// POST /api/activity-tracking/android/grid
// =====================================================================================================

[Collection("Postgres")]
public class AndroidDistinctEntryGridTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    private const long UserId = FakeLoggedUserService.TestUserId;

    private record GridResponse(JsonElement[] Items, int ItemsCount, int PageCount);

    /// <summary>The android twin of <see cref="DesktopDistinctEntryGridTests.Grid_ExcludesOtherUsersEntries"/>.</summary>
    [Fact]
    public async Task Grid_ExcludesOtherUsersEntries()
    {
        var token = DistinctSeed.Token();

        await using (var db = CreateDbContext())
        {
            var other = await ReminderSeedHelper.EnsureOtherUserAsync(db, CancellationToken);

            await DistinctSeed.AndroidSessionAsync(db, UserId, $"com.mine.{token}", "Mine", 0);
            await DistinctSeed.AndroidSessionAsync(db, other.Id, $"com.theirs.{token}", "Theirs", 1);
        }

        var packageNames = PackageNames(await PostAsync(DistinctSeed.UnfilteredBody()));

        packageNames.Should().Contain($"com.mine.{token}");
        packageNames.Should().NotContain($"com.theirs.{token}",
            "another user's android sessions must never reach this grid");
    }

    /// <summary>
    /// The android grouping key is <c>(PackageName, AppLabel)</c> -- two members, not the desktop's three --
    /// so the same package under a relabelled app is two rows, and repeated sessions of one pair are one.
    /// The projected id is <c>g.Min(e =&gt; e.Id)</c> here too.
    /// </summary>
    [Fact]
    public async Task Grid_CollapsesRepeatedEntriesIntoOneRow()
    {
        var token = DistinctSeed.Token();
        var packageName = $"com.collapse.{token}";
        var ids = new List<long>();

        await using (var db = CreateDbContext())
        {
            for (var i = 0; i < 3; i++)
                ids.Add(await DistinctSeed.AndroidSessionAsync(db, UserId, packageName, "Collapse", i));

            await DistinctSeed.AndroidSessionAsync(db, UserId, packageName, "Collapse Renamed", 3);
        }

        var body = await PostAsync(DistinctSeed.FilteredBody(
            new { packageName, packageNameMatchType = DistinctSeed.ExactMatch }));

        body.ItemsCount.Should().Be(2, "four sessions across two distinct app labels are two grid rows");

        var collapsed = body.Items.Single(i => i.GetProperty("appLabel").GetString() == "Collapse");
        collapsed.GetProperty("id").GetInt64().Should().Be(ids.Min(),
            "the row key is the lowest id in the group");
    }

    // ---- B24: coveredBy + coverage ----------------------------------------------------------------

    /// <summary>
    /// B24 on the android grid. ⚠ <c>AndroidSyncEndpoint</c> does not apply the android rules, so here
    /// <c>coveredBy</c> means "an active rule exists that would match" and carries no attribution meaning.
    /// The resolution still goes through <c>AndroidMappingRuleSet</c> — the same specificity-then-id,
    /// first-match, active-only walk as desktop — so when the sync does adopt the rules it resolves from
    /// the same place and the grid stays truthful. Both the inactive and the ignore arms are pinned here
    /// too because the android set orders and filters on its own columns.
    /// </summary>
    [Fact(DisplayName = "B24: android/grid says which active rule would match each pair, null when none")]
    public async Task Grid_CoveredBy_NamesTheMatchingRule()
    {
        var token = DistinctSeed.Token();
        var mapped = $"com.mapped.{token}";
        var narrowed = $"com.narrowed.{token}";
        var inactive = $"com.inactive.{token}";
        var undecided = $"com.undecided.{token}";
        long activityId, packageRuleId, ignoreRuleId;

        await using (var db = CreateDbContext())
        {
            activityId = await ActivityProfileTestSeedHelper.SeedActivityAsync(db, UserId, $"Podcasts {token}");
            packageRuleId = await DistinctSeed.AndroidRuleAsync(db, UserId, packageName: mapped, activityId: activityId);

            // A package-only rule (older) and a package+label ignore rule: the two-column rule must win.
            await DistinctSeed.AndroidRuleAsync(db, UserId, packageName: narrowed, activityId: activityId);
            ignoreRuleId = await DistinctSeed.AndroidRuleAsync(db, UserId, packageName: narrowed, appLabel: "Beta", ignored: true);

            await DistinctSeed.AndroidRuleAsync(db, UserId, packageName: inactive, activityId: activityId, isActive: false);

            await DistinctSeed.AndroidSessionAsync(db, UserId, mapped, "Mapped", 0);
            await DistinctSeed.AndroidSessionAsync(db, UserId, narrowed, "Beta", 1);
            await DistinctSeed.AndroidSessionAsync(db, UserId, inactive, "Inactive", 2);
            await DistinctSeed.AndroidSessionAsync(db, UserId, undecided, "Undecided", 3);
        }

        var body = await PostAsync(DistinctSeed.FilteredBody(
            new { packageName = $".{token}", packageNameMatchType = DistinctSeed.ContainsMatch }));

        JsonElement Row(string packageName) => body.Items.Single(i => i.GetProperty("packageName").GetString() == packageName);

        DistinctSeed.CoveredBy(Row(mapped)).Should().Be((packageRuleId, "Activity", $"Podcasts {token}"));
        DistinctSeed.CoveredBy(Row(narrowed)).Should().Be((ignoreRuleId, "Ignored", (string?)null),
            "package + label constrained beats package alone, whichever is older");
        DistinctSeed.CoveredBy(Row(inactive)).Should().BeNull("an inactive rule covers nothing");
        DistinctSeed.CoveredBy(Row(undecided)).Should().BeNull();
    }

    /// <summary>The android twin of the desktop <c>coverage</c> case — exact count and paging over the in-memory resolve.</summary>
    [Fact(DisplayName = "B24: android/grid coverage=Uncovered / Covered narrow the grid and count exactly")]
    public async Task Grid_CoverageFilter_NarrowsAndCountsExactly()
    {
        var token = DistinctSeed.Token();
        var packageName = $"com.coverage.{token}";

        await using (var db = CreateDbContext())
        {
            var activityId = await ActivityProfileTestSeedHelper.SeedActivityAsync(db, UserId, $"Decided {token}");
            await DistinctSeed.AndroidRuleAsync(db, UserId,
                packageName: packageName, appLabel: "decided", appLabelMatch: PatternMatchType.Contains, activityId: activityId);

            await DistinctSeed.AndroidSessionAsync(db, UserId, packageName, "decided one", 0);
            for (var i = 0; i < 3; i++)
                await DistinctSeed.AndroidSessionAsync(db, UserId, packageName, $"open {i}", 1 + i);
        }

        var uncovered = await PostAsync(DistinctSeed.FilteredBody(
            new { packageName, packageNameMatchType = DistinctSeed.ExactMatch, coverage = "Uncovered" },
            itemsPerPage: 2, page: 2));
        uncovered.ItemsCount.Should().Be(3);
        uncovered.PageCount.Should().Be(2);
        uncovered.Items.Should().ContainSingle("three at two per page leaves one on page 2")
            .Which.GetProperty("appLabel").GetString().Should().StartWith("open ");

        var covered = await PostAsync(DistinctSeed.FilteredBody(
            new { packageName, packageNameMatchType = DistinctSeed.ExactMatch, coverage = "Covered" }));
        covered.ItemsCount.Should().Be(1);
        covered.Items.Single().GetProperty("appLabel").GetString().Should().Be("decided one");
    }

    [Fact(DisplayName = "B24: android/grid rejects an undefined coverage value with a 400")]
    public async Task Grid_CoverageFilter_RejectsAnUndefinedValue()
    {
        var response = await CreateClient().PostAsJsonAsync(DistinctSeed.AndroidGridUrl,
            DistinctSeed.FilteredBody(new { coverage = 99 }), DistinctSeed.Json, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Grid_Unauthenticated_Returns401()
    {
        var response = await CreateUnauthenticatedClient().PostAsJsonAsync(
            DistinctSeed.AndroidGridUrl, DistinctSeed.UnfilteredBody(), DistinctSeed.Json, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ---- helpers ---------------------------------------------------------

    private static List<string> PackageNames(GridResponse body) =>
        body.Items.Select(i => i.GetProperty("packageName").GetString()!).ToList();

    private async Task<GridResponse> PostAsync(object requestBody)
    {
        var response = await CreateClient().PostAsJsonAsync(
            DistinctSeed.AndroidGridUrl, requestBody, DistinctSeed.Json, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<GridResponse>(DistinctSeed.Json, CancellationToken))!;
    }
}

// =====================================================================================================
// GET /api/activity-tracking/desktop/distinct-processes
// =====================================================================================================

[Collection("Postgres")]
public class DesktopDistinctProcessesTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    private const long UserId = FakeLoggedUserService.TestUserId;

    /// <summary>
    /// This one feeds a process picker, so a leak here is a list of the executables another user runs.
    /// It has no paging and no filter -- the hand-written <c>Where(x =&gt; x.UserId == userId)</c> is the
    /// endpoint's only narrowing clause.
    /// </summary>
    [Fact]
    public async Task DistinctProcesses_ExcludesOtherUsersProcesses()
    {
        var token = DistinctSeed.Token();

        await using (var db = CreateDbContext())
        {
            var other = await ReminderSeedHelper.EnsureOtherUserAsync(db, CancellationToken);

            await DistinctSeed.DesktopEntryAsync(db, UserId, $"mine-{token}.exe", "Mine", "my window", 0);
            await DistinctSeed.DesktopEntryAsync(db, other.Id, $"theirs-{token}.exe", "Theirs", "their window", 1);
        }

        var values = await ValuesAsync();

        values.Should().Contain($"mine-{token}.exe");
        values.Should().NotContain($"theirs-{token}.exe",
            "the process picker must not enumerate another user's executables");
    }

    /// <summary>
    /// The title is <c>g.Max(x =&gt; x.ProductName == "" ? null : x.ProductName) ?? g.Key</c>: an empty
    /// product name is mapped to null so the aggregate skips it, and the process name is only the fallback
    /// when <em>every</em> row of the group has one. Dropping the empty-string arm makes an entry whose
    /// product name was never populated present as a blank label, and dropping the coalesce makes it null;
    /// both render as an empty picker row, which is why this asserts against all three candidate values.
    /// </summary>
    [Fact]
    public async Task DistinctProcesses_UseTheFirstNonEmptyProductNameAsTheTitle()
    {
        var token = DistinctSeed.Token();
        var processName = $"titled-{token}.exe";

        await using (var db = CreateDbContext())
        {
            await DistinctSeed.DesktopEntryAsync(db, UserId, processName, "", "unlabelled window", 0);
            await DistinctSeed.DesktopEntryAsync(db, UserId, processName, "Real Product", "labelled window", 1);
        }

        var response = await CreateClient().GetAsync(DistinctSeed.DistinctProcessesUrl, CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var entry = (await response.Content.ReadFromJsonAsync<JsonElement>(CancellationToken))
            .EnumerateArray()
            .Single(e => e.GetProperty("value").GetString() == processName);

        var title = entry.GetProperty("title").GetString();
        title.Should().Be("Real Product");
        title.Should().NotBeEmpty("an empty product name must not win over a populated one");
        title.Should().NotBe(processName, "the process name is only the fallback when no row carries a product name");
    }

    [Fact]
    public async Task DistinctProcesses_Unauthenticated_Returns401()
    {
        var response = await CreateUnauthenticatedClient().GetAsync(DistinctSeed.DistinctProcessesUrl, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ---- helpers ---------------------------------------------------------

    private async Task<List<string>> ValuesAsync()
    {
        var response = await CreateClient().GetAsync(DistinctSeed.DistinctProcessesUrl, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        return (await response.Content.ReadFromJsonAsync<JsonElement>(CancellationToken))
            .EnumerateArray()
            .Select(e => e.GetProperty("value").GetString()!)
            .ToList();
    }
}

// =====================================================================================================
// POST /api/activity-tracking/desktop/fetch-categories-and-roles-by-pattern
// =====================================================================================================

[Collection("Postgres")]
public class DesktopCategoriesAndRolesByPatternTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    private const long UserId = FakeLoggedUserService.TestUserId;

    /// <summary>
    /// Unlike the other three this reads the caller's <b>mappings</b>, not the ledger, and its output is a
    /// pair of picker option lists -- so a scoping regression hands the caller another user's category and
    /// role <em>names</em>, and the ids to reference them by.
    /// </summary>
    [Fact]
    public async Task CategoriesAndRoles_ExcludeOtherUsersMappings()
    {
        var token = DistinctSeed.Token();
        long foreignCategoryId;
        long foreignRoleId;
        long ownCategoryId;
        long ownRoleId;

        await using (var db = CreateDbContext())
        {
            ownCategoryId = await DistinctSeed.CategoryAsync(db, UserId, $"Mine {token}");
            ownRoleId = await DistinctSeed.RoleAsync(db, UserId, $"My role {token}");
            await DistinctSeed.MappingAsync(db, UserId, ownCategoryId, ownRoleId, $"mine-{token}.exe");

            var other = await ReminderSeedHelper.EnsureOtherUserAsync(db, CancellationToken);
            foreignCategoryId = await DistinctSeed.CategoryAsync(db, other.Id, $"Theirs {token}");
            foreignRoleId = await DistinctSeed.RoleAsync(db, other.Id, $"Their role {token}");
            await DistinctSeed.MappingAsync(db, other.Id, foreignCategoryId, foreignRoleId, $"theirs-{token}.exe");
        }

        var body = await PostAsync();

        Ids(body, "categories").Should().Contain(ownCategoryId).And.NotContain(foreignCategoryId);
        Ids(body, "roles").Should().Contain(ownRoleId).And.NotContain(foreignRoleId);
        Texts(body, "categories").Should().NotContain($"Theirs {token}");
        Texts(body, "roles").Should().NotContain($"Their role {token}");
    }

    /// <summary>
    /// The endpoint materialises one row per mapping and then de-duplicates in memory with
    /// <c>DistinctBy(e =&gt; e.Id)</c>. Three mappings sharing one category and spread over two roles are
    /// therefore one category option and two role options -- lose the <c>DistinctBy</c> and the picker
    /// repeats a category once per rule that uses it, which is a display bug nothing else would catch.
    /// The names come from a join, so they are asserted too: a null there is a blank option row.
    /// </summary>
    [Fact]
    public async Task CategoriesAndRoles_ReturnEachOptionOnce()
    {
        var token = DistinctSeed.Token();
        long categoryId;
        long firstRoleId;
        long secondRoleId;

        await using (var db = CreateDbContext())
        {
            categoryId = await DistinctSeed.CategoryAsync(db, UserId, $"Shared {token}");
            firstRoleId = await DistinctSeed.RoleAsync(db, UserId, $"First {token}");
            secondRoleId = await DistinctSeed.RoleAsync(db, UserId, $"Second {token}");

            await DistinctSeed.MappingAsync(db, UserId, categoryId, firstRoleId, $"one-{token}.exe");
            await DistinctSeed.MappingAsync(db, UserId, categoryId, secondRoleId, $"two-{token}.exe");
            await DistinctSeed.MappingAsync(db, UserId, categoryId, firstRoleId, $"three-{token}.exe");
        }

        var body = await PostAsync();

        var categories = Options(body, "categories");
        var roles = Options(body, "roles");

        categories.Where(c => c.Id == categoryId).Should().ContainSingle(
            "three mappings share one category, so it is one option");
        categories.Single(c => c.Id == categoryId).Text.Should().Be($"Shared {token}");

        roles.Where(r => r.Id == firstRoleId).Should().ContainSingle(
            "two mappings share the first role, so it is one option");
        roles.Where(r => r.Id == secondRoleId).Should().ContainSingle();
        roles.Single(r => r.Id == secondRoleId).Text.Should().Be($"Second {token}");
    }

    [Fact]
    public async Task CategoriesAndRoles_Unauthenticated_Returns401()
    {
        var response = await CreateUnauthenticatedClient().PostAsJsonAsync(
            DistinctSeed.CategoriesAndRolesUrl, DistinctSeed.UnfilteredBody(), DistinctSeed.Json, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ---- helpers ---------------------------------------------------------

    private record Option(long Id, string Text);

    private static List<Option> Options(JsonElement body, string property) =>
        body.GetProperty(property)
            .EnumerateArray()
            .Select(e => new Option(e.GetProperty("id").GetInt64(), e.GetProperty("text").GetString()!))
            .ToList();

    private static List<long> Ids(JsonElement body, string property) =>
        Options(body, property).Select(o => o.Id).ToList();

    private static List<string> Texts(JsonElement body, string property) =>
        Options(body, property).Select(o => o.Text).ToList();

    private async Task<JsonElement> PostAsync()
    {
        var response = await CreateClient().PostAsJsonAsync(
            DistinctSeed.CategoriesAndRolesUrl, DistinctSeed.UnfilteredBody(), DistinctSeed.Json, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return await response.Content.ReadFromJsonAsync<JsonElement>(CancellationToken);
    }
}