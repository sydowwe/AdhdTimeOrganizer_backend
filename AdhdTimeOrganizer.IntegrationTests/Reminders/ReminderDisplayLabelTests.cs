using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using AdhdTimeOrganizer.Planning.application.service.reminder;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.Contracts.reminders;
using Sydowwe.Framework.Testing;
using Sydowwe.Reminders.domain.entity;
using Sydowwe.Reminders.domain.@enum;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Reminders;

/// <summary>
/// The display label (<c>reminders-admin/O1</c>), asserted on the rows of every read that names a reminder.
/// <para>
/// It fails silently in two ways. The owner's <see cref="IReminderLabelResolver"/> is matched by template key
/// over an injected <c>IEnumerable</c>, so a resolver the DI scan missed, or a key that drifted from the one
/// <see cref="ReminderRegistrationService"/> registers, leaves every label null and the screens quietly fall
/// back to "Reminder #23001". And the label is filled after projection, endpoint by endpoint, so a read that
/// forgets the post-process step still returns 200 with the field missing.
/// </para>
/// </summary>
[Collection("Postgres")]
public class ReminderDisplayLabelTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    private const string Title = "Call the dentist";
    private const string OwnerModule = "LabelTest";

    [Fact(DisplayName = "Every admin read labels a portal reminder by its title")]
    public async Task AdminReads_CarryThePortalTitle()
    {
        var (labelled, unlabelled) = await SeedPairAsync(recipientUserId: 1);
        await using (var seed = CreateDbContext())
            await SeedFailureAsync(seed, labelled);

        var admin = CreateAdminRoleClient();

        var grid = await PostGridAsync(admin, "api/reminder-definition/grid");
        LabelOf(grid, labelled).Should().Be(Title);
        LabelOf(grid, unlabelled).Should().BeNull("no resolver is registered for that template key, so the client shows the subject");

        var upcoming = await PostGridAsync(admin, "api/reminder-dashboard/upcoming");
        LabelOf(upcoming, labelled).Should().Be(Title);

        var detail = await admin.GetAsync($"api/reminder-definition/{labelled}", CancellationToken);
        detail.StatusCode.Should().Be(HttpStatusCode.OK);
        (await detail.Content.ReadFromJsonAsync<JsonElement>(JsonOpts, CancellationToken))
            .GetProperty("displayLabel").GetString().Should().Be(Title);

        var history = await PostGridAsync(admin, "api/reminder-dashboard/dispatch-history");
        history.GetProperty("items").EnumerateArray()
            .Where(i => i.GetProperty("reminderDefinitionId").GetInt64() == labelled)
            .Should().ContainSingle()
            .Which.GetProperty("displayLabel").GetString().Should().Be(Title);

        var overview = await admin.GetAsync("api/reminder-dashboard/overview", CancellationToken);
        (await overview.Content.ReadFromJsonAsync<JsonElement>(JsonOpts, CancellationToken))
            .GetProperty("failures").EnumerateArray()
            .Where(f => f.GetProperty("reminderDefinitionId").GetInt64() == labelled)
            .Should().ContainSingle()
            .Which.GetProperty("displayLabel").GetString().Should().Be(Title);
    }

    [Fact(DisplayName = "The recipient's own list carries the label but still never the payload")]
    public async Task MyUpcoming_CarriesTheLabel_NotThePayload()
    {
        var (labelled, _) = await SeedPairAsync(recipientUserId: FakeLoggedUserService.TestUserId);

        var mine = await PostGridAsync(CreateUserRoleClient(), "api/reminder-dashboard/my-upcoming");
        var row = mine.GetProperty("items").EnumerateArray()
            .Single(i => i.GetProperty("id").GetInt64() == labelled);

        row.GetProperty("displayLabel").GetString().Should().Be(Title);
        row.TryGetProperty("payloadJson", out _).Should().BeFalse(
            "the payload is projected only for the resolver; the self-service row has never exposed it");
        row.TryGetProperty("subjectId", out _).Should().BeFalse();
    }

    // ---- helpers -----------------------------------------------------------------------------------

    private async Task<(long Labelled, long Unlabelled)> SeedPairAsync(long recipientUserId)
    {
        await using var db = CreateDbContext();
        var labelled = NewDefinition(ReminderRegistrationService.TemplateKey,
            $$"""{"reminderId":1,"title":"{{Title}}","plannerTaskId":null}""", recipientUserId);
        var unlabelled = NewDefinition("label-test.no-resolver", $$"""{"title":"{{Title}}"}""", recipientUserId);
        db.Set<ReminderDefinition>().AddRange(labelled, unlabelled);
        await db.SaveChangesAsync(CancellationToken);
        return (labelled.Id, unlabelled.Id);
    }

    private static ReminderDefinition NewDefinition(string templateKey, string payloadJson, long recipientUserId) => new()
    {
        OwnerModule = OwnerModule,
        SubjectType = "Fixture",
        // The key is uniquely indexed and these tests share a database, so every run needs its own subject.
        SubjectId = Guid.NewGuid().ToString("N"),
        Kind = "PersonalReminder",
        TemplateKey = templateKey,
        PayloadJson = payloadJson,
        ScheduleType = ReminderScheduleType.OneShot,
        DueAt = DateTimeOffset.UtcNow.AddDays(1),
        NextOccurrenceAt = DateTimeOffset.UtcNow.AddDays(1),
        RecipientMode = RecipientMode.ExplicitUsers,
        Recipients = [new ReminderRecipient { UserId = recipientUserId }],
        Status = ReminderStatus.Active
    };

    private async Task SeedFailureAsync(DbContext db, long definitionId)
    {
        db.Set<ReminderDispatch>().Add(new ReminderDispatch
        {
            ReminderDefinitionId = definitionId,
            OccurrenceAt = DateTimeOffset.UtcNow.AddMinutes(-10),
            // Newest in the ledger, so it lands inside the overview's capped failure sample.
            DispatchedAt = DateTimeOffset.UtcNow,
            Outcome = DispatchOutcome.Failed,
            FailureDetail = "Dispatch threw InvalidOperationException.",
            TemplateKeySnapshot = ReminderRegistrationService.TemplateKey,
            CorrelationId = "display-label-test"
        });
        await db.SaveChangesAsync(CancellationToken);
    }

    private async Task<JsonElement> PostGridAsync(HttpClient client, string url)
    {
        // Scoped to this class's owner module: the database is shared, and a page of someone else's rows
        // would push these off it.
        var response = await client.PostAsJsonAsync(url, new
        {
            useFilter = true,
            filter = new { ownerModule = OwnerModule },
            sortBy = Array.Empty<object>(),
            itemsPerPage = 200,
            page = 1
        }, JsonOpts, CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return await response.Content.ReadFromJsonAsync<JsonElement>(JsonOpts, CancellationToken);
    }

    private static string? LabelOf(JsonElement grid, long id) =>
        grid.GetProperty("items").EnumerateArray()
            .Single(i => i.GetProperty("id").GetInt64() == id)
            .GetProperty("displayLabel").GetString();
}
