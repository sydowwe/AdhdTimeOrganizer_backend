using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Reminders.domain.entity;
using Sydowwe.Reminders.domain.@enum;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Reminders;

/// <summary>
/// The recipient's own list says what they already did to a row, and acting twice has a defined outcome
/// (<c>reminders-user/B6</c>, §S7). Before this, a snoozed or dismissed row came back from <c>my-upcoming</c>
/// unchanged, so the client's optimistic drop was undone by its own reload, and a repeat dismiss appended another
/// marker every time.
/// </summary>
[Collection("Postgres")]
public class MyOccurrenceActionTests(AppDbContextFixture fixture) : ReminderAuthTestBase(fixture)
{
    [Fact(DisplayName = "A snoozed row reports Snoozed and where it moved to; an untouched row reports nothing")]
    public async Task MyUpcoming_ReportsASnooze()
    {
        var snoozed = await CreateReminderAsAsync(TestUserId, "Snoozed");
        var untouched = await CreateReminderAsAsync(TestUserId, "Untouched");
        var until = snoozed.NextOccurrenceAt!.Value.AddHours(1);

        (await SnoozeAsync(snoozed, until)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var rows = await MyUpcomingAsync();
        rows[snoozed.DefinitionId].GetProperty("myAction").GetString().Should().Be("Snoozed");
        rows[snoozed.DefinitionId].GetProperty("snoozeUntil").GetDateTimeOffset().Should().Be(until);
        rows[untouched.DefinitionId].GetProperty("myAction").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact(DisplayName = "A dismissed row reports Dismissed, and a repeat dismiss is a 204 that writes nothing")]
    public async Task Dismiss_IsIdempotent()
    {
        var mine = await CreateReminderAsAsync(TestUserId, "Mine");

        (await DismissAsync(mine)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await DismissAsync(mine)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        await using (var db = CreateDbContext())
            (await db.Set<ReminderOccurrenceAction>().CountAsync(CancellationToken))
                .Should().Be(1, "the second dismiss of an already-dismissed occurrence must not append another marker");

        var row = (await MyUpcomingAsync())[mine.DefinitionId];
        row.GetProperty("myAction").GetString().Should().Be("Dismissed");
        row.GetProperty("snoozeUntil").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact(DisplayName = "The latest action wins, so a dismiss after a snooze reads Dismissed")]
    public async Task MyUpcoming_LatestActionWins()
    {
        var mine = await CreateReminderAsAsync(TestUserId, "Mine");

        (await SnoozeAsync(mine, mine.NextOccurrenceAt!.Value.AddHours(1))).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await DismissAsync(mine)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await MyUpcomingAsync())[mine.DefinitionId].GetProperty("myAction").GetString().Should().Be("Dismissed");
    }

    [Fact(DisplayName = "A reversed dismiss is not the effective action, and dismissing again writes a new marker")]
    public async Task MyUpcoming_IgnoresAReversedAction()
    {
        var mine = await CreateReminderAsAsync(TestUserId, "Mine");
        (await DismissAsync(mine)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        await using (var db = CreateDbContext())
        {
            var dismiss = await db.Set<ReminderOccurrenceAction>().SingleAsync(CancellationToken);
            db.Add(new ReminderOccurrenceAction
            {
                ReminderDefinitionId = dismiss.ReminderDefinitionId,
                OccurrenceAt = dismiss.OccurrenceAt,
                UserId = dismiss.UserId,
                ActionType = ReminderActionType.Reversal,
                ActedAt = DateTimeOffset.UtcNow,
                ReversesActionId = dismiss.Id
            });
            await db.SaveChangesAsync(CancellationToken);
        }

        (await MyUpcomingAsync())[mine.DefinitionId].GetProperty("myAction").ValueKind.Should().Be(JsonValueKind.Null);

        (await DismissAsync(mine)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        await using (var db = CreateDbContext())
            (await db.Set<ReminderOccurrenceAction>().CountAsync(a => a.ActionType == ReminderActionType.Dismiss, CancellationToken))
                .Should().Be(2, "the idempotency check reads the effective action, and a reversed dismiss is not one");
    }

    [Fact(DisplayName = "A snooze that lands before its occurrence is refused and writes nothing")]
    public async Task Snooze_BeforeTheOccurrence_Returns400()
    {
        var mine = await CreateReminderAsAsync(TestUserId, "Mine");

        // In the future, so only the new rule can refuse it.
        var response = await SnoozeAsync(mine, mine.NextOccurrenceAt!.Value.AddHours(-1));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync(CancellationToken)).Should().Contain("after the occurrence");

        await using var db = CreateDbContext();
        (await db.Set<ReminderOccurrenceAction>().CountAsync(CancellationToken)).Should().Be(0);
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

    private async Task<Dictionary<long, JsonElement>> MyUpcomingAsync()
    {
        var response = await CreateClient().PostAsJsonAsync("api/reminder-dashboard/my-upcoming", UnfilteredPage(), JsonOpts, CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<JsonElement>(JsonOpts, CancellationToken))
            .GetProperty("items").EnumerateArray()
            .ToDictionary(i => i.GetProperty("id").GetInt64());
    }
}
