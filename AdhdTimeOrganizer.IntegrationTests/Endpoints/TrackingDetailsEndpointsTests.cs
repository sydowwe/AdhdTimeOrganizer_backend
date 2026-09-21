using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AdhdTimeOrganizer.Core.domain.model.entity.user;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using AdhdTimeOrganizer.IntegrationTests.Reminders;
using AdhdTimeOrganizer.Tracking.domain.model.entity.activityTracking;
using AdhdTimeOrganizer.Tracking.domain.model.entity.activityTracking.desktop;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.Testing;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Endpoints;

/// <summary>
/// The three details panels — <c>GET /desktop/process-details</c>,
/// <c>GET /web-extension/domain-details</c> and <c>GET /android/app-details</c> (B23). They are the same
/// endpoint three times with different nouns, so every envelope case here is mirrored across all of
/// them: the original pair drifted apart once already (the span cap was on one and not the other,
/// <c>PERF-3</c>) and neither half of the drift showed up as a failure. The android one has its own
/// cases on top for the figures the other two do not carry — session count, first/last use, the
/// per-device and per-day lists, and the folded-tail sentinel.
///
/// <para>They are also the only <b>GET</b> endpoints among the module's dashboards. Everything binds
/// off the query string via <c>[QueryParam]</c>, and <c>from</c>/<c>to</c> here are an <b>instant
/// envelope</b> — the outer bounds of whatever span the caller had on screen — not the
/// <c>TimeDto</c> time-of-day window every POST dashboard takes. Sending the dashboard shape at these
/// binds nothing and gets a 400 that looks like a validator problem.</para>
///
/// <para>The user's zone is <b>Europe/Bratislava</b>, not the fixture default of UTC. The window-mask
/// members are minutes past midnight <i>on the user's clock</i>, so against a UTC user a correct fold
/// and a missing one produce identical numbers and the mask cases prove nothing.</para>
///
/// <para><see cref="TrackingDashboardDateRangeTests.ProcessDetails_ApplyTheDailyWindowToTheEnvelopeItIsOpenedWith"/>
/// covers the mask on the desktop endpoint against a UTC user; the web-extension twin lives here.</para>
/// </summary>
[Collection("Postgres")]
public class TrackingDetailsEndpointsTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    private const long UserId = FakeLoggedUserService.TestUserId;

    private const string ProcessRoute = "/api/activity-tracking/desktop/process-details";
    private const string DomainRoute = "/api/activity-tracking/web-extension/domain-details";
    private const string AppRoute = "/api/activity-tracking/android/app-details";

    private static readonly TimeZoneInfo Bratislava = TimeZoneInfo.FindSystemTimeZoneById("Europe/Bratislava");

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// A plain single-day envelope for the cases that are not about the envelope. Whole UTC days, so no
    /// case below depends on the user's zone unless it sends the mask.
    /// </summary>
    private static readonly DateTime Day1 = new(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);

    private static readonly string OneDay = Envelope(Day1, Day1.AddDays(1));

    /// <summary>
    /// 09:00-17:00 on the user's clock over three days, as the envelope a dashboard hands over: from
    /// 06-01 09:00 through 06-03 17:00 in Bratislava, which in June is 07:00Z through 15:00Z.
    /// </summary>
    private static readonly string MaskedEnvelope = Envelope(
        new DateTime(2026, 6, 1, 7, 0, 0, DateTimeKind.Utc),
        new DateTime(2026, 6, 3, 15, 0, 0, DateTimeKind.Utc));

    private const string Mask = "&windowStartMinutes=540&windowEndMinutes=1020";

    /// <summary>Midday on the middle day (12:00 Bratislava), inside every reading of the window.</summary>
    private static readonly DateTime InsideWindow = new(2026, 6, 2, 10, 0, 0, DateTimeKind.Utc);

    /// <summary>
    /// 02:00 on the user's clock on the middle night — inside the envelope, inside no daily window. The
    /// zone is what makes this row load-bearing: at 00:00Z it is only outside a 09:00-17:00 window once
    /// the window is resolved on the user's clock rather than on UTC.
    /// </summary>
    private static readonly DateTime InsideEnvelopeOnlyAtNight = new(2026, 6, 2, 0, 0, 0, DateTimeKind.Utc);

    protected override async Task SeedAsync(DbContext db)
    {
        var user = await db.Set<User>().IgnoreQueryFilters().SingleAsync(u => u.Id == UserId);
        user.Timezone = Bratislava;
        await db.SaveChangesAsync();
    }

    // ---- 1. the breakdown is the named item's only ------------------------

    /// <summary>
    /// The panel names one process and must answer for that one alone. A missing process predicate is
    /// invisible on a machine that ran one program and inflates every number on a machine that did not.
    /// </summary>
    [Fact]
    public async Task ProcessDetails_ReturnTheBreakdownForTheNamedProcess()
    {
        await SeedDesktopAsync(
            NewDesktop(Day1.AddHours(10), "code.exe", "a", active: 100, background: 20),
            NewDesktop(Day1.AddHours(10).AddMinutes(1), "code.exe", "b", active: 50, background: 10),
            NewDesktop(Day1.AddHours(10), "chrome.exe", "c", active: 999, background: 999));

        var details = await GetAsync($"{ProcessRoute}?{OneDay}&processName=code.exe");

        details.GetProperty("totalSeconds").GetInt32().Should().Be(180,
            "only the two code.exe rows count -- 150 active plus 30 background");
        details.GetProperty("activeSeconds").GetInt32().Should().Be(150);
        details.GetProperty("backgroundSeconds").GetInt32().Should().Be(30);
        details.GetProperty("entries").GetInt32().Should().Be(2,
            "entries is the number of ledger rows behind the panel, not the number of distinct titles");

        details.GetProperty("windowTitles").EnumerateArray()
            .Sum(t => t.GetProperty("totalSeconds").GetInt32())
            .Should().Be(180,
                "the secondary list is a partition of the same rows the totals came from, so the two " +
                "surfaces can only agree if both were filtered by the same predicate");
    }

    /// <inheritdoc cref="ProcessDetails_ReturnTheBreakdownForTheNamedProcess"/>
    [Fact]
    public async Task DomainDetails_ReturnTheBreakdownForTheNamedDomain()
    {
        await SeedWebAsync(
            NewWeb(Day1.AddHours(10), "github.com", "https://github.com/a", active: 100, background: 20),
            NewWeb(Day1.AddHours(10).AddMinutes(1), "github.com", "https://github.com/b", active: 50, background: 10),
            NewWeb(Day1.AddHours(10), "example.com", "https://example.com/", active: 999, background: 999));

        var details = await GetAsync($"{DomainRoute}?{OneDay}&domain=github.com");

        details.GetProperty("totalSeconds").GetInt32().Should().Be(180);
        details.GetProperty("activeSeconds").GetInt32().Should().Be(150);
        details.GetProperty("backgroundSeconds").GetInt32().Should().Be(30);
        details.GetProperty("entries").GetInt32().Should().Be(2);

        details.GetProperty("pages").EnumerateArray()
            .Sum(p => p.GetProperty("totalSeconds").GetInt32())
            .Should().Be(180,
                "the page list is a partition of the same rows the totals came from");
    }

    /// <summary>
    /// The android twin, over session rows. Every header figure is asserted against a seed where each of
    /// them differs from the others, because a panel that printed the total under "longest session" or
    /// the session count under "devices" would still be a well-formed 200.
    /// </summary>
    [Fact]
    public async Task AppDetails_ReturnTheFiguresForTheNamedApp()
    {
        await SeedAndroidAsync(
            NewAndroid(Day1.AddHours(10), "com.spotify.music", "Spotify", seconds: 100),
            NewAndroid(Day1.AddHours(11), "com.spotify.music", "Spotify", seconds: 50),
            NewAndroid(Day1.AddHours(12), "com.spotify.music", "Spotify", seconds: 30),
            NewAndroid(Day1.AddHours(10), "com.other.app", "Other", seconds: 999));

        var details = await GetAsync($"{AppRoute}?{OneDay}&packageName=com.spotify.music");

        details.GetProperty("packageName").GetString().Should().Be("com.spotify.music");
        details.GetProperty("appLabel").GetString().Should().Be("Spotify");
        details.GetProperty("totalSeconds").GetInt64().Should().Be(180,
            "only the three spotify rows count");
        details.GetProperty("sessions").GetInt32().Should().Be(3,
            "sessions is the number of session rows behind the panel");
        details.GetProperty("meanSessionSeconds").GetInt64().Should().Be(60);
        details.GetProperty("longestSessionSeconds").GetInt64().Should().Be(100);
        details.GetProperty("firstUseUtc").GetDateTime().ToUniversalTime()
            .Should().Be(Day1.AddHours(10), "the earliest session's start");
        details.GetProperty("lastUseUtc").GetDateTime().ToUniversalTime()
            .Should().Be(Day1.AddHours(12).AddSeconds(30), "the latest session's end, not its start");

        details.TryGetProperty("activeSeconds", out _).Should().BeFalse(
            "the android source has no foreground/background split and the response must not invent one");
        details.TryGetProperty("backgroundSeconds", out _).Should().BeFalse();
    }

    // ---- 2. the secondary list ------------------------------------------

    /// <summary>
    /// The android panel's two lists. Devices are ordered by seconds descending, like desktop's window
    /// titles; days are ordered by date, on the user's clock — a 23:30 Bratislava session is 21:30Z, and
    /// bucketing by UTC date would file it under the day before. Both lists partition the same rows the
    /// totals came from.
    /// </summary>
    [Fact]
    public async Task AppDetails_GroupByDeviceAndByLocalDay()
    {
        // 2026-06-02 23:30 in Bratislava (UTC+2 in June) is 21:30Z on the same UTC day; 2026-06-01 23:30
        // local is 21:30Z on 06-01. Envelope is two whole UTC days.
        var lateOnDay1Local = new DateTime(2026, 6, 1, 21, 30, 0, DateTimeKind.Utc);
        var lateOnDay2Local = new DateTime(2026, 6, 2, 21, 30, 0, DateTimeKind.Utc);
        // 2026-06-02 00:30 in Bratislava is 06-01 22:30Z -- a UTC-date bucket would put it on 06-01.
        var earlyOnDay2Local = new DateTime(2026, 6, 1, 22, 30, 0, DateTimeKind.Utc);

        await SeedAndroidAsync(
            NewAndroid(lateOnDay1Local, "com.spotify.music", "Spotify", seconds: 100, deviceId: "phone"),
            NewAndroid(earlyOnDay2Local, "com.spotify.music", "Spotify", seconds: 40, deviceId: "tablet"),
            NewAndroid(lateOnDay2Local, "com.spotify.music", "Spotify", seconds: 10, deviceId: "phone"));

        var details = await GetAsync(
            $"{AppRoute}?{Envelope(Day1, Day1.AddDays(2))}&packageName=com.spotify.music");

        var devices = details.GetProperty("devices").EnumerateArray().ToList();
        devices.Select(d => d.GetProperty("deviceId").GetString())
            .Should().Equal(new[] { "phone", "tablet" }, "ordered by seconds descending");
        devices[0].GetProperty("totalSeconds").GetInt64().Should().Be(110);
        devices[0].GetProperty("sessions").GetInt32().Should().Be(2);
        devices[1].GetProperty("totalSeconds").GetInt64().Should().Be(40);
        devices[1].GetProperty("sessions").GetInt32().Should().Be(1);

        var days = details.GetProperty("days").EnumerateArray().ToList();
        days.Select(d => d.GetProperty("date").GetString())
            .Should().Equal(new[] { "2026-06-01", "2026-06-02" }, "one entry per local day with rows, by date");
        days[0].GetProperty("totalSeconds").GetInt64().Should().Be(100,
            "only the 23:30 session on 06-01 -- the 00:30 one on 06-02 is the next local day even though " +
            "it is still 06-01 in UTC");
        days[0].GetProperty("sessions").GetInt32().Should().Be(1);
        days[1].GetProperty("totalSeconds").GetInt64().Should().Be(50);
        days[1].GetProperty("sessions").GetInt32().Should().Be(2);

        devices.Sum(d => d.GetProperty("totalSeconds").GetInt64())
            .Should().Be(details.GetProperty("totalSeconds").GetInt64());
        days.Sum(d => d.GetProperty("totalSeconds").GetInt64())
            .Should().Be(details.GetProperty("totalSeconds").GetInt64());
    }

    /// <summary>
    /// Window titles are grouped, ordered by time descending, and the two flag-derived numbers come only
    /// from the rows carrying the flag — <c>fullscreenSeconds</c> counting every row of the group would
    /// still be a plausible number and still be wrong.
    /// </summary>
    [Fact]
    public async Task ProcessDetails_GroupTheWindowTitles()
    {
        await SeedDesktopAsync(
            NewDesktop(Day1.AddHours(10), "code.exe", "Docs", active: 100, fullscreen: true),
            NewDesktop(Day1.AddHours(10).AddMinutes(1), "code.exe", "Docs", active: 50, sound: true),
            NewDesktop(Day1.AddHours(10).AddMinutes(2), "code.exe", "Chat", active: 30));

        var details = await GetAsync($"{ProcessRoute}?{OneDay}&processName=code.exe");

        var titles = details.GetProperty("windowTitles").EnumerateArray().ToList();

        titles.Select(t => t.GetProperty("windowTitle").GetString())
            .Should().Equal(new[] { "Docs", "Chat" },
                "three rows across two titles is two entries, ordered by seconds descending");

        titles[0].GetProperty("totalSeconds").GetInt32().Should().Be(150);
        titles[0].GetProperty("fullscreenSeconds").GetInt32().Should().Be(100,
            "only the fullscreen row's own seconds -- not the group's");
        titles[0].GetProperty("soundSeconds").GetInt32().Should().Be(50);

        titles[1].GetProperty("totalSeconds").GetInt32().Should().Be(30);
        titles[1].GetProperty("fullscreenSeconds").GetInt32().Should().Be(0);
        titles[1].GetProperty("soundSeconds").GetInt32().Should().Be(0);
    }

    /// <inheritdoc cref="ProcessDetails_GroupTheWindowTitles"/>
    [Fact]
    public async Task DomainDetails_GroupThePages()
    {
        await SeedWebAsync(
            NewWeb(Day1.AddHours(10), "github.com", "https://github.com/a", active: 100),
            NewWeb(Day1.AddHours(10).AddMinutes(1), "github.com", "https://github.com/a", active: 50),
            NewWeb(Day1.AddHours(10).AddMinutes(2), "github.com", "https://github.com/b", active: 30));

        var details = await GetAsync($"{DomainRoute}?{OneDay}&domain=github.com");

        var pages = details.GetProperty("pages").EnumerateArray().ToList();

        pages.Select(p => p.GetProperty("url").GetString())
            .Should().Equal(new[] { "https://github.com/a", "https://github.com/b" },
                "three rows across two urls is two entries, ordered by seconds descending");

        pages[0].GetProperty("totalSeconds").GetInt32().Should().Be(150);
        pages[1].GetProperty("totalSeconds").GetInt32().Should().Be(30);
    }

    // ---- 3. path extraction ---------------------------------------------

    /// <summary>
    /// <c>path</c> is what the panel actually lists, and it is derived per row rather than stored. All
    /// three branches of <c>ExtractPath</c> are here, including the fallback: a row whose url is not a
    /// url must degrade to the raw string, because throwing out of the projection turns a whole panel
    /// into a 500 over one malformed row a browser extension sent months ago.
    /// </summary>
    [Fact]
    public async Task DomainDetails_ExtractThePath()
    {
        var longPath = "/" + new string('a', 120);
        var expectedTruncation = longPath[..97] + "...";

        await SeedWebAsync(
            NewWeb(Day1.AddHours(10), "github.com", "https://github.com/user/repo?tab=x", active: 100),
            NewWeb(Day1.AddHours(10).AddMinutes(1), "github.com", $"https://github.com{longPath}", active: 50),
            NewWeb(Day1.AddHours(10).AddMinutes(2), "github.com", "not a url", active: 30));

        var details = await GetAsync($"{DomainRoute}?{OneDay}&domain=github.com");

        var paths = details.GetProperty("pages").EnumerateArray()
            .ToDictionary(
                p => p.GetProperty("url").GetString()!,
                p => p.GetProperty("path").GetString());

        paths["https://github.com/user/repo?tab=x"].Should().Be("/user/repo?tab=x",
            "the query string is part of what distinguishes one page from another, so PathAndQuery is " +
            "the right member -- AbsolutePath would collapse every tab of the same repo into one row");

        paths[$"https://github.com{longPath}"].Should().Be(expectedTruncation,
            "a path over 100 characters is cut to 97 plus an ellipsis, so the list stays readable");
        expectedTruncation.Length.Should().Be(100);

        paths["not a url"].Should().Be("not a url",
            "an unparseable url falls back to the raw string rather than throwing out of the projection");
    }

    // ---- 4. the 404 ------------------------------------------------------

    /// <summary>
    /// Both endpoints document a 404 and both reach it through an empty result set rather than a lookup,
    /// so the branch is one <c>records.Count == 0</c> away from answering 200 with a zeroed body. The
    /// client tells "no data for this selection" apart from "this item does not exist" by the status.
    /// </summary>
    [Fact]
    public async Task ProcessDetails_ReturnNotFoundForAProcessWithNoRows()
    {
        await SeedDesktopAsync(NewDesktop(Day1.AddHours(10), "code.exe", "a", active: 100));

        var response = await GetRawAsync($"{ProcessRoute}?{OneDay}&processName=never-ran.exe");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await response.Content.ReadAsStringAsync(CancellationToken))
            .Should().Contain("Process not found.",
                "the 404 carries the error the endpoint added, not an empty body");
    }

    /// <inheritdoc cref="ProcessDetails_ReturnNotFoundForAProcessWithNoRows"/>
    [Fact]
    public async Task DomainDetails_ReturnNotFoundForADomainWithNoRows()
    {
        await SeedWebAsync(NewWeb(Day1.AddHours(10), "github.com", "https://github.com/a", active: 100));

        var response = await GetRawAsync($"{DomainRoute}?{OneDay}&domain=never-visited.test");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await response.Content.ReadAsStringAsync(CancellationToken))
            .Should().Contain("Domain not found.");
    }

    /// <inheritdoc cref="ProcessDetails_ReturnNotFoundForAProcessWithNoRows"/>
    [Fact]
    public async Task AppDetails_ReturnNotFoundForAnAppWithNoRows()
    {
        await SeedAndroidAsync(NewAndroid(Day1.AddHours(10), "com.spotify.music", "Spotify", seconds: 100));

        var response = await GetRawAsync($"{AppRoute}?{OneDay}&packageName=com.never.installed");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await response.Content.ReadAsStringAsync(CancellationToken))
            .Should().Contain("App not found.");
    }

    /// <summary>
    /// The pie folds its sub-threshold tail into a <c>_other</c> row and the client refuses to select it.
    /// Asked anyway, the sentinel is not resolved to the folded apps and not to a row that happens to
    /// carry that package name either — it is a 404 like any other unknown package.
    /// </summary>
    [Fact]
    public async Task AppDetails_ReturnNotFoundForTheFoldedTailSentinel()
    {
        await SeedAndroidAsync(
            NewAndroid(Day1.AddHours(10), "com.spotify.music", "Spotify", seconds: 100),
            NewAndroid(Day1.AddHours(11), "_other", "_other", seconds: 100));

        var response = await GetRawAsync($"{AppRoute}?{OneDay}&packageName=_other");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "the sentinel is not an app, even when a row carrying it exists");
    }

    // ---- 5. cross-user ---------------------------------------------------

    /// <summary>
    /// These two are the only read endpoints in the module that take an <b>arbitrary string identifier
    /// from the caller</b>, which makes them the module's one IDOR-shaped read surface: the process name
    /// or domain is guessable, so the user predicate is the only thing standing between a caller and
    /// somebody else's ledger. Both halves are asserted — an item only the other user has is a 404, and
    /// an item both have answers with the caller's seconds alone.
    /// </summary>
    [Fact]
    public async Task ProcessDetails_DoNotSeeAnotherUsersRows()
    {
        await using (var db = CreateDbContext())
        {
            await ReminderSeedHelper.EnsureOtherUserAsync(db, CancellationToken);

            db.Set<DesktopActivityEntry>().AddRange(
                NewDesktop(Day1.AddHours(10), "shared.exe", "mine", active: 60, userId: UserId),
                NewDesktop(Day1.AddHours(10), "shared.exe", "theirs", active: 9_000,
                    userId: ReminderSeedHelper.OtherUserId),
                NewDesktop(Day1.AddHours(11), "theirs-only.exe", "theirs", active: 9_000,
                    userId: ReminderSeedHelper.OtherUserId));

            await db.SaveChangesAsync(CancellationToken);
        }

        var shared = await GetAsync($"{ProcessRoute}?{OneDay}&processName=shared.exe");
        shared.GetProperty("totalSeconds").GetInt32().Should().Be(60,
            "the other user's 9000 seconds under the same process name are not the caller's");
        shared.GetProperty("entries").GetInt32().Should().Be(1);

        var theirsOnly = await GetRawAsync($"{ProcessRoute}?{OneDay}&processName=theirs-only.exe");
        theirsOnly.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "a process only another user ran does not exist as far as this caller is concerned");
    }

    /// <inheritdoc cref="ProcessDetails_DoNotSeeAnotherUsersRows"/>
    [Fact]
    public async Task DomainDetails_DoNotSeeAnotherUsersRows()
    {
        await using (var db = CreateDbContext())
        {
            await ReminderSeedHelper.EnsureOtherUserAsync(db, CancellationToken);

            db.Set<WebExtensionActivityEntry>().AddRange(
                NewWeb(Day1.AddHours(10), "shared.test", "https://shared.test/mine", active: 60, userId: UserId),
                NewWeb(Day1.AddHours(10), "shared.test", "https://shared.test/theirs", active: 9_000,
                    userId: ReminderSeedHelper.OtherUserId),
                NewWeb(Day1.AddHours(11), "theirs-only.test", "https://theirs-only.test/", active: 9_000,
                    userId: ReminderSeedHelper.OtherUserId));

            await db.SaveChangesAsync(CancellationToken);
        }

        var shared = await GetAsync($"{DomainRoute}?{OneDay}&domain=shared.test");
        shared.GetProperty("totalSeconds").GetInt32().Should().Be(60,
            "browsing history is the most sensitive ledger in the module and the domain is guessable");
        shared.GetProperty("entries").GetInt32().Should().Be(1);

        var theirsOnly = await GetRawAsync($"{DomainRoute}?{OneDay}&domain=theirs-only.test");
        theirsOnly.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <inheritdoc cref="ProcessDetails_DoNotSeeAnotherUsersRows"/>
    [Fact]
    public async Task AppDetails_DoNotSeeAnotherUsersRows()
    {
        await using (var db = CreateDbContext())
        {
            await ReminderSeedHelper.EnsureOtherUserAsync(db, CancellationToken);

            db.Set<AndroidSessionData>().AddRange(
                NewAndroid(Day1.AddHours(10), "com.shared.app", "Shared", seconds: 60, userId: UserId),
                NewAndroid(Day1.AddHours(10), "com.shared.app", "Shared", seconds: 9_000,
                    deviceId: "their-phone", userId: ReminderSeedHelper.OtherUserId),
                NewAndroid(Day1.AddHours(11), "com.theirs.only", "Theirs", seconds: 9_000,
                    deviceId: "their-phone", userId: ReminderSeedHelper.OtherUserId));

            await db.SaveChangesAsync(CancellationToken);
        }

        var shared = await GetAsync($"{AppRoute}?{OneDay}&packageName=com.shared.app");
        shared.GetProperty("totalSeconds").GetInt64().Should().Be(60,
            "the other user's 9000 seconds under the same package are not the caller's");
        shared.GetProperty("sessions").GetInt32().Should().Be(1);
        shared.GetProperty("devices").GetArrayLength().Should().Be(1,
            "the other user's device must not appear in the caller's device list either");

        var theirsOnly = await GetRawAsync($"{AppRoute}?{OneDay}&packageName=com.theirs.only");
        theirsOnly.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ---- 6 & 8. the daily window mask -----------------------------------

    /// <summary>
    /// The web-extension twin of
    /// <see cref="TrackingDashboardDateRangeTests.ProcessDetails_ApplyTheDailyWindowToTheEnvelopeItIsOpenedWith"/>.
    /// The panel is opened with the outer envelope of a multi-day selection, which covers the nights the
    /// dashboard's own time-of-day window excludes; given that window back, it counts the same time the
    /// slice it was opened from did.
    /// </summary>
    [Fact]
    public async Task DomainDetails_ApplyTheDailyWindowMask()
    {
        await SeedMaskFixtureAsync();

        var details = await GetAsync($"{DomainRoute}?{MaskedEnvelope}&domain=github.com{Mask}");

        details.GetProperty("totalSeconds").GetInt32().Should().Be(600,
            "09:00-17:00 on the user's clock keeps the midday row and drops the 02:00 one -- resolving " +
            "those minutes against UTC instead would fold a different set of nights in");
        details.GetProperty("entries").GetInt32().Should().Be(1);
    }

    /// <summary>
    /// The other direction, and the reason the mask exists: the same seed without it answers with the
    /// whole envelope, night included. Asserting only the masked number would pass just as well against
    /// an endpoint that dropped the night rows for some unrelated reason.
    /// </summary>
    [Fact]
    public async Task DomainDetails_TakeTheWholeEnvelopeWhenNoMaskIsSent()
    {
        await SeedMaskFixtureAsync();

        var details = await GetAsync($"{DomainRoute}?{MaskedEnvelope}&domain=github.com");

        details.GetProperty("totalSeconds").GetInt32().Should().Be(6_600,
            "with no window named the envelope is continuous, so the 02:00 row on the middle night " +
            "counts -- this is the over-count the mask members exist to remove");
        details.GetProperty("entries").GetInt32().Should().Be(2);
    }

    /// <summary>
    /// The android twin of <see cref="DomainDetails_ApplyTheDailyWindowMask"/> and
    /// <see cref="DomainDetails_TakeTheWholeEnvelopeWhenNoMaskIsSent"/> in one: the same seed, asked
    /// both ways. A session is counted whole by where it <i>starts</i>, the same rule desktop's entries
    /// follow, so the 02:00 session is dropped by the mask and kept without it.
    /// </summary>
    [Fact]
    public async Task AppDetails_ApplyTheDailyWindowMaskAndTakeTheWholeEnvelopeWithoutIt()
    {
        await SeedAndroidAsync(
            NewAndroid(InsideWindow, "com.spotify.music", "Spotify", seconds: 600),
            NewAndroid(InsideEnvelopeOnlyAtNight, "com.spotify.music", "Spotify", seconds: 6_000));

        var masked = await GetAsync($"{AppRoute}?{MaskedEnvelope}&packageName=com.spotify.music{Mask}");
        masked.GetProperty("totalSeconds").GetInt64().Should().Be(600,
            "09:00-17:00 on the user's clock keeps the midday session and drops the 02:00 one");
        masked.GetProperty("sessions").GetInt32().Should().Be(1);
        masked.GetProperty("days").GetArrayLength().Should().Be(1,
            "the day list is built from the masked rows, so the night session does not add a day");

        var unmasked = await GetAsync($"{AppRoute}?{MaskedEnvelope}&packageName=com.spotify.music");
        unmasked.GetProperty("totalSeconds").GetInt64().Should().Be(6_600,
            "with no window named the envelope is continuous and the 02:00 session counts");
        unmasked.GetProperty("sessions").GetInt32().Should().Be(2);
    }

    // ---- 7. half a mask --------------------------------------------------

    /// <summary>
    /// The window is optional but not half-optional: one bound without the other has no reading, and
    /// silently ignoring the half that arrived would put the panel back to over-counting with nothing to
    /// show it had. Both directions, on both endpoints.
    /// </summary>
    [Theory]
    [InlineData(ProcessRoute, "processName=code.exe", "&windowStartMinutes=540")]
    [InlineData(ProcessRoute, "processName=code.exe", "&windowEndMinutes=1020")]
    [InlineData(DomainRoute, "domain=github.com", "&windowStartMinutes=540")]
    [InlineData(DomainRoute, "domain=github.com", "&windowEndMinutes=1020")]
    [InlineData(AppRoute, "packageName=com.spotify.music", "&windowStartMinutes=540")]
    [InlineData(AppRoute, "packageName=com.spotify.music", "&windowEndMinutes=1020")]
    public async Task Details_RejectHalfAWindowMask_With400(string route, string identifier, string halfMask)
    {
        var response = await GetRawAsync($"{route}?{MaskedEnvelope}&{identifier}{halfMask}");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "a lone bound is rejected rather than dropped -- dropping it answers 200 with the " +
            "unmasked total, which is the exact number the caller was trying to avoid");
    }

    // ---- 9 & 10. the span cap -------------------------------------------

    /// <summary>
    /// The details span cap. Narrower than the dashboards' year because this read is filtered by a
    /// single process or domain and nothing else, so the span is the only thing bounding how much of the
    /// partitioned ledger one request walks.
    /// </summary>
    [Theory]
    [InlineData(ProcessRoute, "processName=code.exe")]
    [InlineData(DomainRoute, "domain=github.com")]
    [InlineData(AppRoute, "packageName=com.spotify.music")]
    public async Task Details_RejectARangeLongerThan31Days_With400(string route, string identifier)
    {
        var response = await GetRawAsync(
            $"{route}?{Envelope(Day1, Day1.AddDays(34))}&{identifier}");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    /// <summary>
    /// <c>PERF-3</c>. The cap used to be on the web-extension validator alone, so a ten-year envelope at
    /// the desktop endpoint was accepted and read the partitioned desktop ledger unbounded. Both
    /// validators now share <c>DashboardDateRangeRules.ApplyDetailsDateRangeRules</c>; this pins the
    /// desktop half specifically, because the drift was invisible from either file on its own.
    /// </summary>
    [Fact]
    public async Task ProcessDetails_RejectAnUnboundedRange_With400()
    {
        var response = await GetRawAsync(
            $"{ProcessRoute}?{Envelope(new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc), new DateTime(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc))}&processName=code.exe");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "a ten-year envelope walks every partition of the desktop ledger in one request");
    }

    // ---- 11. the identifier ---------------------------------------------

    /// <summary>
    /// The identifier is the whole point of the panel. Empty, the read degenerates to "every row in the
    /// envelope that happens to carry an empty name" — which on the desktop ledger is a plausible-looking
    /// answer rather than an obviously broken one.
    /// </summary>
    [Theory]
    [InlineData(ProcessRoute, "processName=")]
    [InlineData(ProcessRoute, "")]
    [InlineData(DomainRoute, "domain=")]
    [InlineData(DomainRoute, "")]
    [InlineData(AppRoute, "packageName=")]
    [InlineData(AppRoute, "")]
    public async Task Details_RejectAnEmptyIdentifier_With400(string route, string identifier)
    {
        var response = await GetRawAsync($"{route}?{OneDay}&{identifier}");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ---- helpers ---------------------------------------------------------

    /// <summary>
    /// Ten minutes at midday and a hundred at two in the morning, both on the middle day of the
    /// envelope. The night row is the larger one on purpose: a mask that silently does nothing reports
    /// eleven times the right number rather than a number close enough to read as rounding.
    /// </summary>
    private Task SeedMaskFixtureAsync() => SeedWebAsync(
        NewWeb(InsideWindow, "github.com", "https://github.com/day", active: 600),
        NewWeb(InsideEnvelopeOnlyAtNight, "github.com", "https://github.com/night", active: 6_000));

    private async Task SeedDesktopAsync(params DesktopActivityEntry[] rows)
    {
        await using var db = CreateDbContext();
        db.Set<DesktopActivityEntry>().AddRange(rows);
        await db.SaveChangesAsync(CancellationToken);
    }

    private async Task SeedWebAsync(params WebExtensionActivityEntry[] rows)
    {
        await using var db = CreateDbContext();
        db.Set<WebExtensionActivityEntry>().AddRange(rows);
        await db.SaveChangesAsync(CancellationToken);
    }

    private async Task SeedAndroidAsync(params AndroidSessionData[] rows)
    {
        await using var db = CreateDbContext();
        db.Set<AndroidSessionData>().AddRange(rows);
        await db.SaveChangesAsync(CancellationToken);
    }

    private static AndroidSessionData NewAndroid(
        DateTime sessionStart,
        string packageName,
        string appLabel,
        long seconds,
        string deviceId = "phone",
        long userId = UserId)
    {
        var start = DateTime.SpecifyKind(sessionStart, DateTimeKind.Utc);
        return new AndroidSessionData
        {
            UserId = userId,
            DeviceId = deviceId,
            PackageName = packageName,
            AppLabel = appLabel,
            SessionStartUtc = start,
            SessionEndUtc = start.AddSeconds(seconds),
            DurationSeconds = seconds
        };
    }

    private static DesktopActivityEntry NewDesktop(
        DateTime windowStart,
        string processName,
        string windowTitle,
        int active,
        int background = 0,
        bool fullscreen = false,
        bool sound = false,
        int monitor = 0,
        long userId = UserId) => new()
    {
        UserId = userId,
        WindowStart = DateTime.SpecifyKind(windowStart, DateTimeKind.Utc),
        ProcessName = processName,
        ProductName = processName,
        WindowTitle = windowTitle,
        ExecutablePath = $"/usr/bin/{processName}",
        IsFullscreen = fullscreen,
        IsPlayingSound = sound,
        ActiveSeconds = active,
        BackgroundSeconds = background,
        ActiveMonitor = monitor
    };

    private static WebExtensionActivityEntry NewWeb(
        DateTime windowStart,
        string domain,
        string? url,
        int active,
        int background = 0,
        long userId = UserId) => new()
    {
        UserId = userId,
        WindowStart = DateTime.SpecifyKind(windowStart, DateTimeKind.Utc),
        Domain = domain,
        Url = url,
        ActiveSeconds = active,
        BackgroundSeconds = background,
        IsFinal = true
    };

    /// <summary>
    /// The envelope members, written the way a client writes them — an explicit <c>Z</c>. An offset or a
    /// bare local instant binds to a different <c>DateTimeKind</c>, which is
    /// <c>DailyWindowMaskRequest.AsUtcInstant</c>'s problem and not covered here.
    /// </summary>
    private static string Envelope(DateTime from, DateTime to) =>
        $"from={from:yyyy-MM-dd'T'HH:mm:ss'Z'}&to={to:yyyy-MM-dd'T'HH:mm:ss'Z'}";

    private async Task<JsonElement> GetAsync(string route)
    {
        var response = await GetRawAsync(route);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        return await response.Content.ReadFromJsonAsync<JsonElement>(Json, CancellationToken);
    }

    private Task<HttpResponseMessage> GetRawAsync(string route) =>
        CreateUserRoleClient().GetAsync(route, TestContext.Current.CancellationToken);
}
