using System.Net;
using System.Net.Http.Json;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using AdhdTimeOrganizer.IntegrationTests.Reminders;
using AdhdTimeOrganizer.Routines.application.dto.response.todoList;
using AdhdTimeOrganizer.Routines.domain.model.entity.todoList;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.Testing;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Endpoints;

/// <summary>
/// B5 / item 2 — the weekly routine review dismissal. It used to be one browser's <c>localStorage</c> key, so
/// what matters is that the value is per <i>user</i> and survives to another device: written once, it reads
/// back on any client of the same account, and never on another account's.
/// <para>
/// Note the deliberate difference from <c>UserPlannerSettingsTests</c>: a GET here creates <b>no</b> row.
/// </para>
/// </summary>
[Collection("Postgres")]
public class UserRoutineSettingsTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    private const long UserId = FakeLoggedUserService.TestUserId;
    private const string SettingsUrl = "api/routine/settings";

    [Fact(DisplayName = "A user who never dismissed anything reads null, and no row is created")]
    public async Task MissingRow_ReadsNull_AndStaysMissing()
    {
        var response = await CreateClient().GetAsync(SettingsUrl, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var settings = await response.Content.ReadFromJsonAsync<UserRoutineSettingsResponse>(JsonOpts, CancellationToken);
        settings!.RoutineReviewDismissedForWeekStart.Should().BeNull();

        await using var db = CreateDbContext();
        (await db.Set<UserRoutineSettings>().CountAsync(s => s.UserId == UserId, CancellationToken))
            .Should().Be(0, "reading the settings must not write a row that holds nothing");
    }

    [Fact(DisplayName = "A dismissal written on one client reads back on another, from one row")]
    public async Task Dismissal_RoundTrips_AndUpsertsASingleRow()
    {
        var laptop = CreateClient();
        var write = await laptop.PutAsJsonAsync(SettingsUrl,
            new { RoutineReviewDismissedForWeekStart = "2026-08-17" }, JsonOpts, CancellationToken);
        write.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // A different HttpClient over the same account — the "desktop" in the bug report.
        var desktop = CreateClient();
        var settings = await ReadAsync(desktop);
        settings.RoutineReviewDismissedForWeekStart.Should().Be(new DateOnly(2026, 8, 17));

        var second = await desktop.PutAsJsonAsync(SettingsUrl,
            new { RoutineReviewDismissedForWeekStart = "2026-08-24" }, JsonOpts, CancellationToken);
        second.StatusCode.Should().Be(HttpStatusCode.NoContent);

        await using var db = CreateDbContext();
        var rows = await db.Set<UserRoutineSettings>().AsNoTracking()
            .Where(s => s.UserId == UserId).ToListAsync(CancellationToken);
        rows.Should().ContainSingle("the second write updates the same row rather than inserting a second one");
        rows[0].RoutineReviewDismissedForWeekStart.Should().Be(new DateOnly(2026, 8, 24));
    }

    [Fact(DisplayName = "null clears the dismissal, bringing the card back")]
    public async Task NullClearsTheDismissal()
    {
        var client = CreateClient();
        await client.PutAsJsonAsync(SettingsUrl, new { RoutineReviewDismissedForWeekStart = "2026-08-17" }, JsonOpts, CancellationToken);

        var clear = await client.PutAsJsonAsync(SettingsUrl,
            new { RoutineReviewDismissedForWeekStart = (string?)null }, JsonOpts, CancellationToken);
        clear.StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await ReadAsync(client)).RoutineReviewDismissedForWeekStart.Should().BeNull();
    }

    [Fact(DisplayName = "A full ISO instant is accepted and lands on the date the client meant")]
    public async Task IsoInstant_IsReadAsUtcDate()
    {
        // A client that round-trips the week-start through a JavaScript Date sends this shape; read as UTC it
        // must stay 2026-08-17, not slip to the 16th on a server west of Greenwich.
        var client = CreateClient();
        var write = await client.PutAsJsonAsync(SettingsUrl,
            new { RoutineReviewDismissedForWeekStart = "2026-08-17T00:00:00.000Z" }, JsonOpts, CancellationToken);
        write.StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await ReadAsync(client)).RoutineReviewDismissedForWeekStart.Should().Be(new DateOnly(2026, 8, 17));
    }

    [Fact(DisplayName = "A value that is not a date is a 400, not a silently dropped dismissal")]
    public async Task UnparseableValue_Returns400()
    {
        var response = await CreateClient().PutAsJsonAsync(SettingsUrl,
            new { RoutineReviewDismissedForWeekStart = "last monday" }, JsonOpts, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact(DisplayName = "One account's dismissal is invisible to another account")]
    public async Task AnotherUsersDismissal_IsNotVisible()
    {
        await using (var db = CreateDbContext())
        {
            await ReminderSeedHelper.EnsureOtherUserAsync(db, CancellationToken);
            db.Set<UserRoutineSettings>().Add(new UserRoutineSettings
            {
                UserId = ReminderSeedHelper.OtherUserId,
                RoutineReviewDismissedForWeekStart = new DateOnly(2026, 8, 17)
            });
            await db.SaveChangesAsync(CancellationToken);
        }

        (await ReadAsync(CreateClient())).RoutineReviewDismissedForWeekStart.Should().BeNull();

        // And writing ours must not touch theirs.
        await CreateClient().PutAsJsonAsync(SettingsUrl,
            new { RoutineReviewDismissedForWeekStart = "2026-09-07" }, JsonOpts, CancellationToken);

        await using var verify = CreateDbContext();
        var theirs = await verify.Set<UserRoutineSettings>().AsNoTracking().IgnoreQueryFilters()
            .SingleAsync(s => s.UserId == ReminderSeedHelper.OtherUserId, CancellationToken);
        theirs.RoutineReviewDismissedForWeekStart.Should().Be(new DateOnly(2026, 8, 17));
    }

    [Fact(DisplayName = "The settings endpoints require authentication")]
    public async Task Unauthenticated_Returns401()
    {
        var client = CreateUnauthenticatedClient();

        (await client.GetAsync(SettingsUrl, CancellationToken)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await client.PutAsJsonAsync(SettingsUrl, new { RoutineReviewDismissedForWeekStart = "2026-08-17" }, JsonOpts, CancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private async Task<UserRoutineSettingsResponse> ReadAsync(HttpClient client)
    {
        var response = await client.GetAsync(SettingsUrl, CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<UserRoutineSettingsResponse>(JsonOpts, CancellationToken))!;
    }
}
