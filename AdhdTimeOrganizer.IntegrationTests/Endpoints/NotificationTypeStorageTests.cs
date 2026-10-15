using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.Contracts.notification;
using Sydowwe.Framework.Contracts.reminders;
using Sydowwe.Framework.Testing;
using Sydowwe.Notifications.application.dto;
using Sydowwe.Notifications.domain.entity;
using Sydowwe.Reminders.domain.entity;
using Sydowwe.Reminders.domain.@enum;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Endpoints;

/// <summary>
/// R1: <see cref="NotificationType"/> stopped being an enum and became a key a slice declares. Nothing persisted
/// was supposed to change — the four columns held the enum's member name as text, and now hold the key, which is
/// that same name. These pin the bytes, both ways, against rows written the way the old code wrote them: raw SQL,
/// with no converter of ours in the path.
/// <para>
/// And the one behaviour that <i>was</i> supposed to change: the enum converter parsed a stored name back into a
/// member, so a member deleted from the enum made every stored row of its kind unreadable — the bell 500'd. A key
/// nothing registers now reads back as itself and renders the generic text.
/// </para>
/// </summary>
[Collection("Postgres")]
public class NotificationTypeStorageTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    private const long UserId = FakeLoggedUserService.TestUserId;

    [Fact(DisplayName = "A preference row stored under a kind's name is read back as that kind, and honoured")]
    public async Task PreferenceRow_WrittenAsText_ReadsBackAsTheKind()
    {
        await using (var db = CreateDbContext())
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT INTO notifications.notification_preference (user_id, type, channel, enabled) VALUES ({UserId}, 'TimerBoundary', 'WebPush', false)",
                CancellationToken);

        await using (var db = CreateDbContext())
        {
            var stored = await db.Set<NotificationPreference>().AsNoTracking().SingleAsync(p => p.UserId == UserId, CancellationToken);
            stored.Type.Should().Be(NotificationType.TimerBoundary);

            (await db.Set<NotificationPreference>().AsNoTracking()
                    .CountAsync(p => p.Type == NotificationType.TimerBoundary && p.Channel == NotificationChannel.WebPush, CancellationToken))
                .Should().Be(1, "the key is compared in SQL, as the dispatcher's preference read does");
        }

        var matrix = await CreateClient().GetFromJsonAsync<List<NotificationPreferenceItem>>(
            "api/notification-preference/mine", JsonOpts, CancellationToken);
        matrix!.Single(i => i.Type == NotificationType.TimerBoundary && i.Channel == NotificationChannel.WebPush).Enabled
            .Should().BeFalse("the stored opt-out is the user's, and it must survive the type change");
    }

    [Fact(DisplayName = "A preference written through the API is stored as the kind's bare name")]
    public async Task PreferenceRow_WrittenThroughTheApi_StoresTheBareName()
    {
        var response = await CreateClient().PutAsJsonAsync("api/notification-preference",
            new { type = "RoutinePeriodEnded", channel = "Email", enabled = true }, JsonOpts, CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        await using var db = CreateDbContext();
        (await db.Database.SqlQuery<string>(
                    $"SELECT type AS \"Value\" FROM notifications.notification_preference WHERE user_id = {UserId}")
                .SingleAsync(CancellationToken))
            .Should().Be("RoutinePeriodEnded");
    }

    [Fact(DisplayName = "A reminder definition's and a dispatch's kind round-trip as text, and null stays null")]
    public async Task ReminderColumns_RoundTripAsText()
    {
        long typedId, untypedId, dispatchId;
        await using (var db = CreateDbContext())
        {
            var typed = Definition("typed", NotificationType.TodoItemDue);
            var untyped = Definition("untyped", null);
            db.AddRange(typed, untyped);
            await db.SaveChangesAsync(CancellationToken);

            var dispatch = new ReminderDispatch
            {
                ReminderDefinitionId = typed.Id,
                OccurrenceAt = DateTimeOffset.UtcNow,
                DispatchedAt = DateTimeOffset.UtcNow,
                Outcome = DispatchOutcome.Sent,
                NotificationTypeSnapshot = NotificationType.TodoItemDue,
                RecipientsSnapshot = $"[{UserId}]",
                CorrelationId = Guid.NewGuid().ToString()
            };
            db.Add(dispatch);
            await db.SaveChangesAsync(CancellationToken);
            (typedId, untypedId, dispatchId) = (typed.Id, untyped.Id, dispatch.Id);
        }

        await using (var db = CreateDbContext())
        {
            (await db.Database.SqlQuery<string?>($"SELECT notification_type AS \"Value\" FROM reminders.reminder_definition WHERE id = {typedId}")
                .SingleAsync(CancellationToken)).Should().Be("TodoItemDue", "the write side is the old enum's member name, byte for byte");
            (await db.Database.SqlQuery<string?>($"SELECT notification_type AS \"Value\" FROM reminders.reminder_definition WHERE id = {untypedId}")
                .SingleAsync(CancellationToken)).Should().BeNull("a renderer-resolved definition has no kind, and must not get an empty one");
            (await db.Database.SqlQuery<string?>($"SELECT notification_type_snapshot AS \"Value\" FROM reminders.reminder_dispatch WHERE id = {dispatchId}")
                .SingleAsync(CancellationToken)).Should().Be("TodoItemDue");

            // The read side, against a value the old code wrote.
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE reminders.reminder_definition SET notification_type = 'PersonalReminder' WHERE id = {typedId}", CancellationToken);
        }

        await using (var db = CreateDbContext())
        {
            var definitions = await db.Set<ReminderDefinition>().AsNoTracking()
                .Where(d => d.Id == typedId || d.Id == untypedId).ToDictionaryAsync(d => d.Id, CancellationToken);
            definitions[typedId].NotificationType.Should().Be(NotificationType.PersonalReminder);
            definitions[untypedId].NotificationType.Should().BeNull();

            (await db.Set<ReminderDispatch>().AsNoTracking().SingleAsync(d => d.Id == dispatchId, CancellationToken))
                .NotificationTypeSnapshot.Should().Be(NotificationType.TodoItemDue);
        }
    }

    /// <summary>
    /// A stored row of a kind no longer shipped — or of one whose slice dropped out of the composition — is a
    /// notification the user received. It stays in their bell, worded generically, and the list does not fail.
    /// </summary>
    [Fact(DisplayName = "A stored notification of a kind nothing registers is listed with the generic text, not a 500")]
    public async Task UnknownKind_IsListedWithTheGenericText()
    {
        await using (var db = CreateDbContext())
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT INTO notifications.notification (user_id, type, payload_json, is_read) VALUES ({UserId}, 'NoSuchKindAnyMore', '{{\"count\":3}}'::jsonb, false)",
                CancellationToken);

        var response = await CreateClient().GetAsync("api/notification/mine", CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK, "an unknown stored kind must not take the whole bell list down");

        var rows = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken)).RootElement.EnumerateArray().ToList();
        var row = rows.Should().ContainSingle().Which;
        row.GetProperty("type").GetString().Should().Be("NoSuchKindAnyMore", "the stored key is echoed as-is, never rewritten");
        // The fixture account reads in English.
        row.GetProperty("title").GetString().Should().Be("New notification");
        row.GetProperty("body").GetString().Should().Be("You have a new notification.");
    }

    private static ReminderDefinition Definition(string subjectId, NotificationType? type) => new()
    {
        OwnerModule = "R1Storage",
        SubjectType = "Probe",
        SubjectId = subjectId,
        Kind = "Probe",
        TemplateKey = "r1.probe",
        PayloadJson = "{}",
        NotificationType = type,
        ScheduleType = ReminderScheduleType.OneShot,
        DueAt = DateTimeOffset.UtcNow.AddDays(1),
        RecipientMode = RecipientMode.ExplicitUsers,
        Status = ReminderStatus.Active,
        NextOccurrenceAt = DateTimeOffset.UtcNow.AddDays(1)
    };
}
