using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AdhdTimeOrganizer.Core.domain.model.entity.user;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using AdhdTimeOrganizer.Tracking.domain.model.entity.activityTracking;
using AdhdTimeOrganizer.Tracking.domain.model.entity.activityTracking.desktop;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.Testing;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Endpoints;

/// <summary>
/// The Tracking counterpart of <see cref="HistoryDashboardUserZoneTests"/>, covering the half of the
/// timezone contract that lives on the <b>way out</b> rather than the way in.
///
/// <para>Threading <c>User.Timezone</c> into the request window was only half the job for these
/// dashboards. They also derive calendar facts <i>from the stored instants</i> — which band a one-minute
/// ledger row belongs to, which weekday it fell on, how many days a stretch of history spans — and every
/// one of those was read straight off a UTC <c>DateTime</c>. So a user east of Greenwich got bars whose
/// boundaries were an offset out of step with their own labels, and baseline averages divided by the
/// wrong day count.</para>
///
/// <para>Both the desktop and the web-extension dashboards are exercised. Their aggregation code is
/// duplicated line for line (two private <c>AlignToWindow</c> copies, two <c>GetBaselineAverages</c>
/// copies), so a fix applied to one and not the other compiles perfectly and is invisible until someone
/// opens the other tab.</para>
/// </summary>
[Collection("Postgres")]
public class TrackingDashboardUserZoneTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    private const long UserId = FakeLoggedUserService.TestUserId;

    private static readonly TimeZoneInfo Bratislava = TimeZoneInfo.FindSystemTimeZoneById("Europe/Bratislava");

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    protected override async Task SeedAsync(DbContext db)
    {
        var user = await db.Set<User>().IgnoreQueryFilters().SingleAsync(u => u.Id == UserId);
        user.Timezone = Bratislava;
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Ninety-minute bands, chosen deliberately: with a band width that divides evenly into the hour, a
    /// whole-hour offset like Bratislava's leaves UTC-aligned and user-aligned boundaries in the same
    /// place, and the bug hides. 90 does not divide the offset, so the two readings land on different
    /// boundaries and the test can tell them apart.
    ///
    /// <para>The row sits at 09:05 on the user's clock. Aligned to their day that is the band opening at
    /// 09:00 — 07:00Z in July. Aligned to the UTC instant (07:05Z) it is the band opening at 06:00Z, an
    /// hour earlier and not a boundary the user's clock has at all.</para>
    /// </summary>
    [Theory]
    [InlineData("/api/activity-tracking/desktop/stacked-bars")]
    [InlineData("/api/activity-tracking/web-extension/stacked-bars")]
    public async Task StackedBars_AlignBandsToTheUsersClock_NotUtc(string route)
    {
        await SeedEntryAsync(new DateTime(2026, 7, 15, 7, 5, 0, DateTimeKind.Utc));

        var windows = await PostAsync(route, new
        {
            date = new DateOnly(2026, 7, 15),
            from = new { hours = 0, minutes = 0 },
            to = new { hours = 23, minutes = 59 },
            windowMinutes = 90
        });

        var starts = windows.EnumerateArray()
            .Select(w => w.GetProperty("windowStart").GetDateTimeOffset())
            .ToList();

        starts.Should().ContainSingle().Which.Should().Be(
            new DateTimeOffset(2026, 7, 15, 7, 0, 0, TimeSpan.Zero),
            "09:05 on the user's clock falls in the 90-minute band that opens at their 09:00, which is " +
            "07:00Z in July -- a band computed by flooring the raw UTC instant opens at 06:00Z instead, " +
            "so every bar is offset from the label drawn above it");

        windows.EnumerateArray().First().GetProperty("windowEnd").GetDateTimeOffset().Should().Be(
            new DateTimeOffset(2026, 7, 15, 8, 30, 0, TimeSpan.Zero),
            "the far edge is the user's 10:30, resolved through the zone in its own right rather than " +
            "taken as the start plus 90 minutes -- the two differ across a DST transition");
    }

    /// <summary>
    /// The <c>SameWeekday</c> baseline asks a calendar question of instants, twice: once to decide which
    /// weekday the requested period is, and once per row to decide whether it matches. Both used the UTC
    /// calendar.
    ///
    /// <para>The requested period is Tuesday 2026-07-14 on the user's clock, which begins at 22:00Z on
    /// <i>Monday</i> the 13th — so the target weekday itself came out Monday, and a baseline row logged at
    /// midday on the previous Tuesday was excluded from its own baseline. <c>steady</c> is the process that
    /// catches that. <c>midnight</c> covers the row side of the same reading — its baseline row is half past
    /// midnight on a Tuesday for the user but still Monday in UTC — and is here to hold the row-side
    /// conversion in place, not because it discriminates on its own.</para>
    ///
    /// <para><c>isNew</c> is the assertion because it is exactly "this process had no baseline": the
    /// averages hang off it, so a dropped baseline row shows up as a card claiming the user has never run
    /// the thing before.</para>
    /// </summary>
    [Fact]
    public async Task SummaryCards_MatchTheSameWeekdayBaselineOnTheUsersCalendar_NotUtcs()
    {
        // Current period: Tuesday 2026-07-14, midday for the user.
        await SeedEntryAsync(new DateTime(2026, 7, 14, 10, 0, 0, DateTimeKind.Utc), "steady");
        await SeedEntryAsync(new DateTime(2026, 7, 14, 10, 0, 0, DateTimeKind.Utc), "midnight");

        // Baseline, both on a Tuesday for the user and both inside the 56-day lookback.
        // 2026-07-07 12:00 local — Tuesday in UTC as well.
        await SeedEntryAsync(new DateTime(2026, 7, 7, 10, 0, 0, DateTimeKind.Utc), "steady");
        // 2026-07-07 00:30 local — still Monday the 6th in UTC.
        await SeedEntryAsync(new DateTime(2026, 7, 6, 22, 30, 0, DateTimeKind.Utc), "midnight");

        var cards = await PostAsync("/api/activity-tracking/desktop/summary-cards", new
        {
            date = new DateOnly(2026, 7, 14),
            from = new { hours = 0, minutes = 0 },
            to = new { hours = 23, minutes = 59 },
            baseline = "SameWeekday",
            topN = 10
        });

        var isNewByProcess = cards.EnumerateArray()
            .ToDictionary(c => c.GetProperty("processName").GetString()!, c => c.GetProperty("isNew").GetBoolean());

        isNewByProcess.Should().ContainKey("steady");
        isNewByProcess["steady"].Should().BeFalse(
            "the requested Tuesday starts at 22:00Z on Monday, so reading the target weekday off the raw " +
            "instant makes it Monday and throws away a baseline row logged at midday on the previous " +
            "Tuesday -- the card then reports a process the user runs weekly as brand new");

        isNewByProcess.Should().ContainKey("midnight");
        isNewByProcess["midnight"].Should().BeFalse(
            "half past midnight on the user's Tuesday is still Monday in UTC, so the per-row weekday must " +
            "be read through the zone too");
    }

    // ---- helpers ---------------------------------------------------------

    /// <summary>
    /// Seeds the same instant into both ledgers, so one seeding path serves the desktop and web-extension
    /// cases of the <see cref="StackedBars_AlignBandsToTheUsersClock_NotUtc"/> theory. The web-extension
    /// row keys on domain where the desktop row keys on process name; the name is reused for both.
    /// </summary>
    private async Task SeedEntryAsync(DateTime windowStartUtc, string name = "chrome")
    {
        await using var db = CreateDbContext();

        db.Set<DesktopActivityEntry>().Add(new DesktopActivityEntry
        {
            UserId = UserId,
            WindowStart = windowStartUtc,
            ProcessName = name,
            ProductName = name,
            WindowTitle = "t",
            ExecutablePath = $"/usr/bin/{name}",
            IsFullscreen = false,
            ActiveSeconds = 60,
            BackgroundSeconds = 0,
            IsPlayingSound = false,
            ActiveMonitor = 0
        });

        db.Set<WebExtensionActivityEntry>().Add(new WebExtensionActivityEntry
        {
            UserId = UserId,
            WindowStart = windowStartUtc,
            Domain = $"{name}.example",
            Url = null,
            ActiveSeconds = 60,
            BackgroundSeconds = 0,
            IsFinal = true
        });

        await db.SaveChangesAsync(CancellationToken);
    }

    private async Task<JsonElement> PostAsync(string route, object body)
    {
        var response = await CreateUserRoleClient().PostAsJsonAsync(route, body, Json, CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        return await response.Content.ReadFromJsonAsync<JsonElement>(Json, CancellationToken);
    }
}
