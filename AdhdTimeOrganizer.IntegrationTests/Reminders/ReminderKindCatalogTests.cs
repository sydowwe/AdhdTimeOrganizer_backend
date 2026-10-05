using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using AdhdTimeOrganizer.Planning.application.service.reminder;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.Testing;
using Sydowwe.Reminders.domain.entity;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Reminders;

/// <summary>
/// <c>GET /reminder-preference</c> offers every kind the deployment can raise, not only the ones the caller has
/// already muted (<c>notifications/B3</c>, §S2). Before this the <i>Reminder types</i> card could only ever show
/// its empty state, because a row appeared only after something outside the UI had written one.
/// </summary>
[Collection("Postgres")]
public class ReminderKindCatalogTests(AppDbContextFixture fixture) : ReminderAuthTestBase(fixture)
{
    private const string Personal = ReminderRegistrationService.Kind;

    [Fact(DisplayName = "A user who has set nothing is still offered the personal reminder kind, switched on")]
    public async Task Get_OffersTheDeclaredKind_WithNoStoredRow()
    {
        var payload = await GetPreferencesAsync(CreateClient());

        payload.GetProperty("kindPreferences").GetArrayLength().Should().Be(0, "nothing is stored, and kindPreferences means exactly that");
        var personal = payload.GetProperty("kinds").EnumerateArray().Single(k => k.GetProperty("kind").GetString() == Personal);
        personal.GetProperty("enabled").GetBoolean().Should().BeTrue("absence means enabled");
        personal.GetProperty("ownerModule").GetString().Should().Be(ReminderRegistrationService.OwnerModule);
    }

    [Fact(DisplayName = "Muting a catalog kind is stored, and the catalog then reports it off")]
    public async Task Put_ThenGet_ReportsTheKindOff()
    {
        var client = CreateClient();
        (await client.PutAsJsonAsync("api/reminder-preference/kind", Body(Personal, false), JsonOpts, CancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        var payload = await GetPreferencesAsync(client);

        KindEnabled(payload, Personal).Should().BeFalse();
        payload.GetProperty("kindPreferences").GetArrayLength().Should().Be(1);
        await using var db = CreateDbContext();
        (await db.Set<ReminderKindPreference>().SingleAsync(p => p.Kind == Personal, CancellationToken)).Enabled.Should().BeFalse();
    }

    [Fact(DisplayName = "Switching a muted kind back on is reported on, and keeps one row")]
    public async Task Put_BackOn_ReportsTheKindOn()
    {
        var client = CreateClient();
        (await client.PutAsJsonAsync("api/reminder-preference/kind", Body(Personal, false), JsonOpts, CancellationToken)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await client.PutAsJsonAsync("api/reminder-preference/kind", Body(Personal, true), JsonOpts, CancellationToken)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        KindEnabled(await GetPreferencesAsync(client), Personal).Should().BeTrue();
        await using var db = CreateDbContext();
        (await db.Set<ReminderKindPreference>().CountAsync(CancellationToken)).Should().Be(1);
    }

    [Fact(DisplayName = "A stored opt-out for a kind nobody declares stays in the list, so it can still be switched back on")]
    public async Task Get_KeepsAStoredRowForAnUndeclaredKind()
    {
        var client = CreateClient();
        (await client.PutAsJsonAsync("api/reminder-preference/kind", Body("Retired", false, "Legacy"), JsonOpts, CancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        var kinds = (await GetPreferencesAsync(client)).GetProperty("kinds").EnumerateArray().ToList();

        kinds.Should().Contain(k => k.GetProperty("kind").GetString() == "Retired"
                                    && k.GetProperty("ownerModule").GetString() == "Legacy"
                                    && !k.GetProperty("enabled").GetBoolean());
        kinds.Should().Contain(k => k.GetProperty("kind").GetString() == Personal, "the declared kind is still offered beside it");
    }

    [Fact(DisplayName = "Another user's opt-out does not switch the kind off for the caller, and the list has no duplicates")]
    public async Task Get_IsSelfScoped_AndDeduplicated()
    {
        await using (var seed = CreateDbContext())
            await ReminderSeedHelper.EnsureOtherUserAsync(seed, CancellationToken);
        await using (var otherFactory = CreateFactory(TestRoles.User, OtherUserId))
            (await otherFactory.CreateClient().PutAsJsonAsync("api/reminder-preference/kind", Body(Personal, false), JsonOpts, CancellationToken))
                .StatusCode.Should().Be(HttpStatusCode.NoContent);

        var payload = await GetPreferencesAsync(CreateClient());

        payload.GetProperty("kinds").EnumerateArray().Count(k => k.GetProperty("kind").GetString() == Personal).Should().Be(1);
        KindEnabled(payload, Personal).Should().BeTrue("the other user muted it, not the caller");
    }

    private static object Body(string kind, bool enabled, string ownerModule = ReminderRegistrationService.OwnerModule) =>
        new { OwnerModule = ownerModule, Kind = kind, Enabled = enabled };

    private static bool KindEnabled(JsonElement payload, string kind) =>
        payload.GetProperty("kinds").EnumerateArray().Single(k => k.GetProperty("kind").GetString() == kind).GetProperty("enabled").GetBoolean();

    private async Task<JsonElement> GetPreferencesAsync(HttpClient client)
    {
        var response = await client.GetAsync("api/reminder-preference", CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return await response.Content.ReadFromJsonAsync<JsonElement>(JsonOpts, CancellationToken);
    }
}
