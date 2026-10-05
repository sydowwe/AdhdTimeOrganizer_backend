using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.Testing;
using Sydowwe.Reminders.domain.entity;
using Sydowwe.Reminders.domain.@enum;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Reminders;

/// <summary>
/// A recipient can take back their own snooze or dismiss, and can list the rows they have acted on
/// (<c>reminders-user/O1</c>). Before this the Dismiss confirm called itself final ("cannot be undone from here")
/// and <c>my-upcoming</c> could be narrowed only by columns the caller never set. Every assertion reads rows, not
/// routes: an undo that returned 204 and wrote nothing would pass any status-only test.
/// </summary>
[Collection("Postgres")]
public class UndoOccurrenceActionTests(AppDbContextFixture fixture) : ReminderAuthTestBase(fixture)
{
    [Fact(DisplayName = "Undoing a dismiss appends a reversal pointing at it, and the row reads as untouched again")]
    public async Task Undo_ReversesADismiss()
    {
        var mine = await CreateReminderAsAsync(TestUserId, "Mine");
        (await DismissAsync(mine)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await UndoAsync(mine)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        await using var db = CreateDbContext();
        var rows = await db.Set<ReminderOccurrenceAction>().OrderBy(a => a.Id).ToListAsync(CancellationToken);
        rows.Should().HaveCount(2, "the history is kept: an undo appends, it never deletes the dismiss");
        rows[1].ActionType.Should().Be(ReminderActionType.Reversal);
        rows[1].ReversesActionId.Should().Be(rows[0].Id);

        (await MyUpcomingAsync())[mine.DefinitionId].GetProperty("myAction").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact(DisplayName = "Undoing a snooze clears it and its snoozeUntil")]
    public async Task Undo_ReversesASnooze()
    {
        var mine = await CreateReminderAsAsync(TestUserId, "Mine");
        (await SnoozeAsync(mine, mine.NextOccurrenceAt!.Value.AddHours(1))).StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await UndoAsync(mine)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var row = (await MyUpcomingAsync())[mine.DefinitionId];
        row.GetProperty("myAction").ValueKind.Should().Be(JsonValueKind.Null);
        row.GetProperty("snoozeUntil").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact(DisplayName = "Undoing twice writes one reversal; undoing with nothing to undo is a 204 that writes nothing")]
    public async Task Undo_IsIdempotent()
    {
        var mine = await CreateReminderAsAsync(TestUserId, "Mine");

        (await UndoAsync(mine)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        await using (var db = CreateDbContext())
            (await db.Set<ReminderOccurrenceAction>().CountAsync(CancellationToken)).Should().Be(0, "nothing was acted on");

        (await DismissAsync(mine)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await UndoAsync(mine)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await UndoAsync(mine)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        await using var again = CreateDbContext();
        (await again.Set<ReminderOccurrenceAction>().CountAsync(a => a.ActionType == ReminderActionType.Reversal, CancellationToken))
            .Should().Be(1, "the second undo found no effective action, so it had nothing to reverse");
    }

    [Fact(DisplayName = "A dismiss after an undo is effective again, and a second undo reverses that one")]
    public async Task Undo_ThenActAgain()
    {
        var mine = await CreateReminderAsAsync(TestUserId, "Mine");
        (await DismissAsync(mine)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await UndoAsync(mine)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await DismissAsync(mine)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await MyUpcomingAsync())[mine.DefinitionId].GetProperty("myAction").GetString().Should().Be("Dismissed");

        (await UndoAsync(mine)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await MyUpcomingAsync())[mine.DefinitionId].GetProperty("myAction").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact(DisplayName = "A snooze that has already come due cannot be undone, and nothing is written")]
    public async Task Undo_ADueSnooze_Returns400()
    {
        var mine = await CreateReminderAsAsync(TestUserId, "Mine");

        // The API refuses a snooze that is already due, so seed the marker the scanner is about to re-deliver.
        await using (var db = CreateDbContext())
        {
            db.Add(new ReminderOccurrenceAction
            {
                ReminderDefinitionId = mine.DefinitionId,
                OccurrenceAt = mine.NextOccurrenceAt!.Value,
                UserId = TestUserId,
                ActionType = ReminderActionType.Snooze,
                SnoozeUntil = DateTimeOffset.UtcNow.AddMinutes(-5),
                ActedAt = DateTimeOffset.UtcNow.AddHours(-1)
            });
            await db.SaveChangesAsync(CancellationToken);
        }

        var response = await UndoAsync(mine);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync(CancellationToken)).Should().Contain("already come due");
        await using var check = CreateDbContext();
        (await check.Set<ReminderOccurrenceAction>().CountAsync(a => a.ActionType == ReminderActionType.Reversal, CancellationToken))
            .Should().Be(0);
    }

    [Fact(DisplayName = "An occurrence that is not the pending one is a 400 and writes nothing")]
    public async Task Undo_WrongOccurrence_Returns400()
    {
        var mine = await CreateReminderAsAsync(TestUserId, "Mine");
        (await DismissAsync(mine)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var response = await CreateClient().PostAsJsonAsync("api/reminder-dashboard/undo", new
        {
            ReminderDefinitionId = mine.DefinitionId,
            OccurrenceAt = mine.NextOccurrenceAt!.Value.AddDays(1)
        }, JsonOpts, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        await using var db = CreateDbContext();
        (await db.Set<ReminderOccurrenceAction>().CountAsync(a => a.ActionType == ReminderActionType.Reversal, CancellationToken))
            .Should().Be(0);
    }

    [Fact(DisplayName = "Undo is self-scoped: another user's reminder is a 404 and their dismiss stays in force")]
    public async Task Undo_AnotherUsersReminder_Returns404()
    {
        var theirs = await CreateReminderAsAsync(OtherUserId, "Theirs");
        await using (var otherFactory = CreateFactory(TestRoles.User, OtherUserId))
            (await otherFactory.CreateClient().PostAsJsonAsync("api/reminder-dashboard/dismiss", new
            {
                ReminderDefinitionId = theirs.DefinitionId,
                OccurrenceAt = theirs.NextOccurrenceAt
            }, JsonOpts, CancellationToken)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await UndoAsync(theirs)).StatusCode.Should().Be(HttpStatusCode.NotFound);

        await using var db = CreateDbContext();
        (await db.Set<ReminderOccurrenceAction>().CountAsync(a => a.ActionType == ReminderActionType.Reversal, CancellationToken))
            .Should().Be(0, "the caller is not a recipient, so they cannot reverse anyone's marker");
    }

    [Fact(DisplayName = "Filtering on myAction returns the snoozed rows, or the dismissed ones, and counts only those")]
    public async Task MyUpcoming_FiltersByMyAction()
    {
        var snoozed = await CreateReminderAsAsync(TestUserId, "Snoozed");
        var dismissed = await CreateReminderAsAsync(TestUserId, "Dismissed");
        var untouched = await CreateReminderAsAsync(TestUserId, "Untouched");
        (await SnoozeAsync(snoozed, snoozed.NextOccurrenceAt!.Value.AddHours(1))).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await DismissAsync(dismissed)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var onlySnoozed = await MyUpcomingFilteredAsync("Snoozed");
        onlySnoozed.Items.Keys.Should().BeEquivalentTo([snoozed.DefinitionId]);
        onlySnoozed.Total.Should().Be(1, "the total is computed over the filtered set, so a pager does not promise rows the filter drops");

        var onlyDismissed = await MyUpcomingFilteredAsync("Dismissed");
        onlyDismissed.Items.Keys.Should().BeEquivalentTo([dismissed.DefinitionId]);

        (await MyUpcomingAsync()).Keys.Should().Contain([snoozed.DefinitionId, dismissed.DefinitionId, untouched.DefinitionId]);
    }

    [Fact(DisplayName = "The myAction filter follows the effective action: latest wins, and undoing it re-exposes the one beneath")]
    public async Task MyUpcoming_FilterFollowsTheEffectiveAction()
    {
        var mine = await CreateReminderAsAsync(TestUserId, "Mine");
        (await SnoozeAsync(mine, mine.NextOccurrenceAt!.Value.AddHours(1))).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await DismissAsync(mine)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await MyUpcomingFilteredAsync("Snoozed")).Items.Should().BeEmpty("the later dismiss superseded the snooze");
        (await MyUpcomingFilteredAsync("Dismissed")).Items.Keys.Should().BeEquivalentTo([mine.DefinitionId]);

        (await UndoAsync(mine)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await MyUpcomingFilteredAsync("Dismissed")).Items.Should().BeEmpty("the dismiss was reversed");
        // An undo takes back one action. The scanner's rule is "latest non-reversed wins", so the snooze the
        // dismiss had superseded stands again, and the list says so (as does the scanner's own reading of it).
        (await MyUpcomingFilteredAsync("Snoozed")).Items.Keys.Should().BeEquivalentTo([mine.DefinitionId]);
        (await MyUpcomingAsync())[mine.DefinitionId].GetProperty("myAction").GetString().Should().Be("Snoozed");
    }

    [Fact(DisplayName = "The myAction filter never reaches another user's markers")]
    public async Task MyUpcoming_Filter_IsSelfScoped()
    {
        var mine = await CreateReminderAsAsync(TestUserId, "Mine");
        var theirs = await CreateReminderAsAsync(OtherUserId, "Theirs");
        await using (var otherFactory = CreateFactory(TestRoles.User, OtherUserId))
            (await otherFactory.CreateClient().PostAsJsonAsync("api/reminder-dashboard/dismiss", new
            {
                ReminderDefinitionId = theirs.DefinitionId,
                OccurrenceAt = theirs.NextOccurrenceAt
            }, JsonOpts, CancellationToken)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await MyUpcomingFilteredAsync("Dismissed")).Items.Should().BeEmpty("the only dismissed reminder belongs to someone else");
        (await MyUpcomingAsync()).Keys.Should().Contain(mine.DefinitionId);
    }

    private Task<HttpResponseMessage> SnoozeAsync(ReminderFixture reminder, DateTimeOffset until) =>
        CreateClient().PostAsJsonAsync("api/reminder-dashboard/snooze", new
        {
            ReminderDefinitionId = reminder.DefinitionId,
            OccurrenceAt = reminder.NextOccurrenceAt,
            SnoozeUntil = until
        }, JsonOpts, CancellationToken);

    private Task<HttpResponseMessage> DismissAsync(ReminderFixture reminder) =>
        CreateClient().PostAsJsonAsync("api/reminder-dashboard/dismiss", new
        {
            ReminderDefinitionId = reminder.DefinitionId,
            OccurrenceAt = reminder.NextOccurrenceAt
        }, JsonOpts, CancellationToken);

    private Task<HttpResponseMessage> UndoAsync(ReminderFixture reminder) =>
        CreateClient().PostAsJsonAsync("api/reminder-dashboard/undo", new
        {
            ReminderDefinitionId = reminder.DefinitionId,
            OccurrenceAt = reminder.NextOccurrenceAt
        }, JsonOpts, CancellationToken);

    private async Task<Dictionary<long, JsonElement>> MyUpcomingAsync() => (await FetchAsync(UnfilteredPage())).Items;

    private Task<(Dictionary<long, JsonElement> Items, int Total)> MyUpcomingFilteredAsync(string myAction) =>
        FetchAsync(new
        {
            useFilter = true,
            filter = new { myAction },
            sortBy = Array.Empty<object>(),
            itemsPerPage = 100,
            page = 1
        });

    private async Task<(Dictionary<long, JsonElement> Items, int Total)> FetchAsync(object body)
    {
        var response = await CreateClient().PostAsJsonAsync("api/reminder-dashboard/my-upcoming", body, JsonOpts, CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var grid = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOpts, CancellationToken);
        return (grid.GetProperty("items").EnumerateArray().ToDictionary(i => i.GetProperty("id").GetInt64()),
            grid.GetProperty("itemsCount").GetInt32());
    }
}
