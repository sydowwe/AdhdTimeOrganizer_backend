using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using FluentAssertions;
using Sydowwe.Framework.Contracts.reminders;
using Sydowwe.Framework.Testing;
using Sydowwe.Reminders.domain.entity;
using Sydowwe.Reminders.domain.@enum;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Reminders;

/// <summary>
/// The recipient's own list says how often a recurring reminder repeats (<c>reminders-user/B4</c>). The personal
/// projection is hand-written and narrower than the admin one, so a field it forgets is not an error: the row
/// still returns 200 and the client draws "—" for the cadence, which is how it shipped.
/// </summary>
[Collection("Postgres")]
public class MyUpcomingScheduleTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    private const string OwnerModule = "ScheduleTest";
    private const string Cron = "0 0 9 ? * MON";

    [Fact(DisplayName = "The recipient's own list carries the interval preset and the cron")]
    public async Task MyUpcoming_CarriesTheCadence()
    {
        long interval, cron;
        await using (var db = CreateDbContext())
        {
            var intervalDef = NewDefinition(ReminderScheduleType.RecurringInterval, d => d.IntervalPreset = ReminderIntervalPreset.Monthly);
            var cronDef = NewDefinition(ReminderScheduleType.RecurringCron, d => d.Cron = Cron);
            db.Set<ReminderDefinition>().AddRange(intervalDef, cronDef);
            await db.SaveChangesAsync(CancellationToken);
            (interval, cron) = (intervalDef.Id, cronDef.Id);
        }

        var response = await CreateUserRoleClient().PostAsJsonAsync("api/reminder-dashboard/my-upcoming", new
        {
            useFilter = true,
            filter = new { ownerModule = OwnerModule },
            sortBy = Array.Empty<object>(),
            itemsPerPage = 200,
            page = 1
        }, JsonOpts, CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var items = (await response.Content.ReadFromJsonAsync<JsonElement>(JsonOpts, CancellationToken))
            .GetProperty("items").EnumerateArray().ToList();

        var intervalRow = items.Single(i => i.GetProperty("id").GetInt64() == interval);
        intervalRow.GetProperty("intervalPreset").GetString().Should().Be(nameof(ReminderIntervalPreset.Monthly));

        var cronRow = items.Single(i => i.GetProperty("id").GetInt64() == cron);
        cronRow.GetProperty("cron").GetString().Should().Be(Cron);
    }

    private static ReminderDefinition NewDefinition(ReminderScheduleType scheduleType, Action<ReminderDefinition> cadence)
    {
        var definition = new ReminderDefinition
        {
            OwnerModule = OwnerModule,
            SubjectType = "Fixture",
            // The key is uniquely indexed and these tests share a database, so every run needs its own subject.
            SubjectId = Guid.NewGuid().ToString("N"),
            Kind = "PersonalReminder",
            TemplateKey = "schedule-test",
            ScheduleType = scheduleType,
            AnchorDate = DateTimeOffset.UtcNow,
            NextOccurrenceAt = DateTimeOffset.UtcNow.AddDays(1),
            RecipientMode = RecipientMode.ExplicitUsers,
            Recipients = [new ReminderRecipient { UserId = FakeLoggedUserService.TestUserId }],
            Status = ReminderStatus.Active
        };
        cadence(definition);
        return definition;
    }
}
