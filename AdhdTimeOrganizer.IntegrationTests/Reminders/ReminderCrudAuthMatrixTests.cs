using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using AdhdTimeOrganizer.Planning.application.service.reminder;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.Testing;
using Sydowwe.Reminders.domain.entity;
using Sydowwe.Reminders.domain.@enum;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Reminders;

/// <summary>
/// TEST-2 — the auth matrix for every reminder route this host actually serves, minus the four that already
/// have one.
/// <para>
/// <b>What was already covered.</b> <see cref="ReminderEndpointTests"/> subclasses the framework bases for
/// <c>POST/PUT/DELETE/GET api/reminder</c> (401, role gate, cross-user → 404 via <c>AppDbContext</c>'s global
/// user query filter) and <see cref="ReminderRegistrationTests"/> asserts the same three verbs against a real
/// second user's row. Re-deriving those here would only duplicate them, so this file starts where they stop.
/// </para>
/// <para>
/// <b>What was not.</b> Two whole surfaces:
/// </para>
/// <list type="number">
/// <item>The portal day view, <c>GET api/reminder/by-date/{date}</c> — a hand-written endpoint rather than a
/// framework base, so nothing about its role gate or its scoping was pinned.</item>
/// <item><b>The entire <c>Sydowwe.Reminders</c> module endpoint surface</b> — 14 routes under
/// <c>api/reminder-dashboard</c>, <c>api/reminder-preference</c> and <c>api/reminder-definition</c>. They are
/// live in this host (<c>Program.cs</c> lists <c>typeof(ReminderDefinition).Assembly</c> in FastEndpoints'
/// <c>o.Assemblies</c>), they are all gated at <c>GetUserRole()</c> — i.e. any signed-in user — and
/// <c>ReminderDefinition</c> is <c>BaseTableEntity</c>, <b>not</b> <c>IEntityWithUser</c>, so the global query
/// filter that makes every portal reminder route 404 a foreign id does not cover a single one of them. Each
/// one either scopes by hand or does not scope at all.</item>
/// </list>
/// <para>
/// That second point is why this file is split into "self-scoped" and "not scoped" sections. The endpoints in
/// <see cref="ReminderSelfServiceAuthTests"/> and <see cref="ReminderPreferenceAuthTests"/> filter by
/// <c>User.GetId()</c>, so each is asserted airtight against a second user's rows. The ones in
/// <see cref="ReminderDefinitionAdminOnlyTests"/> do not filter at all and are not meant to — they are the
/// module's cross-user admin inspector — so there the role gate <i>is</i> the boundary, and those tests hold
/// it in place from both sides: refused for a plain User, still cross-user for an Admin.
/// </para>
/// <para>
/// <b>Writing this suite turned up nine routes gated at <c>GetUserRole()</c> that the module's own
/// <c>summary.md</c> specifies as Admin</b> — including <c>cancel</c>/<c>pause</c>/<c>resume</c>, which take a
/// client-supplied <c>ReminderKey</c> and hand it to a registry with no notion of a caller. The code had
/// drifted from its spec; the gates were corrected to <c>GetAdminRole()</c> alongside these tests.
/// </para>
/// </summary>
public abstract class ReminderAuthTestBase(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    protected const long TestUserId = FakeLoggedUserService.TestUserId;
    protected const long OtherUserId = ReminderSeedHelper.OtherUserId;

    /// <summary>
    /// A reminder owned by <paramref name="userId"/>, created through the real <c>POST api/reminder</c> as that
    /// user, so the module-side <see cref="ReminderDefinition"/> and its explicit
    /// <c>ReminderRecipient</c> row exist exactly as production would write them. Seeding the portal row
    /// directly (<see cref="ReminderSeedHelper.SeedReminderAsync"/>) skips
    /// <see cref="ReminderRegistrationService"/> entirely and leaves no definition at all, which is no use to
    /// any of the module-endpoint tests below.
    /// </summary>
    protected async Task<ReminderFixture> CreateReminderAsAsync(long userId, string title)
    {
        if (userId != TestUserId)
        {
            await using var seed = CreateDbContext();
            await ReminderSeedHelper.EnsureOtherUserAsync(seed, CancellationToken);
        }

        await using var factory = CreateFactory(TestRoles.User, userId);
        var response = await factory.CreateClient().PostAsJsonAsync("api/reminder", new
        {
            Title = title,
            // Far enough out that the occurrence is still pending when the assertions run — snooze and dismiss
            // both reject an occurrence that is already in the past.
            RemindAt = DateTime.UtcNow.AddDays(2),
            LeadOffsetsMinutes = new[] { 0 }
        }, JsonOpts, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Created,
            "the fixture itself must succeed before any auth assertion built on it means anything");
        var reminderId = await response.Content.ReadFromJsonAsync<long>(CancellationToken);

        await using var db = CreateDbContext();
        var definition = await DefinitionForAsync(db, reminderId);
        return new ReminderFixture(reminderId, definition.Id, definition.NextOccurrenceAt);
    }

    protected Task<ReminderDefinition> DefinitionForAsync(DbContext db, long reminderId) =>
        db.Set<ReminderDefinition>().SingleAsync(
            d => d.OwnerModule == ReminderRegistrationService.OwnerModule
                 && d.SubjectType == ReminderRegistrationService.SubjectType
                 && d.SubjectId == reminderId.ToString()
                 && d.Kind == ReminderRegistrationService.Kind,
            CancellationToken);

    /// <summary>The module's 4-tuple idempotency key for a portal reminder, as the cancel/pause/resume routes take it.</summary>
    protected static object KeyRequestFor(long reminderId) => new
    {
        OwnerModule = ReminderRegistrationService.OwnerModule,
        SubjectType = ReminderRegistrationService.SubjectType,
        SubjectId = reminderId.ToString(),
        Kind = ReminderRegistrationService.Kind
    };

    /// <summary>A <c>BaseFilterSortPaginateRequest</c> body with filtering off — the "give me everything" page.</summary>
    protected static object UnfilteredPage() => new
    {
        useFilter = false,
        filter = new { },
        sortBy = Array.Empty<object>(),
        itemsPerPage = 100,
        page = 1
    };

    protected static long[] GridIds(JsonElement grid) =>
        grid.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetInt64()).ToArray();

    protected record ReminderFixture(long ReminderId, long DefinitionId, DateTimeOffset? NextOccurrenceAt);
}

// =====================================================================================================
// GetByDateReminderEndpoint ("api/reminder/by-date/{date}") — the day view.
//
// A plain FastEndpoints endpoint, not one of the framework read bases, so none of the base-driven auth
// tests reach it and none of ReminderEndpointTests' four classes cover it. Its own scoping is the global
// query filter (its comment says so), which is exactly the kind of claim that holds until someone adds an
// IgnoreQueryFilters() — hence an assertion on rows rather than on the route.
// =====================================================================================================

[Collection("Postgres")]
public class ReminderByDateAuthTests(AppDbContextFixture fixture) : ReminderAuthTestBase(fixture)
{
    private static string Url(DateOnly date) => $"api/reminder/by-date/{date:yyyy-MM-dd}";

    [Fact(DisplayName = "The day view rejects an anonymous caller")]
    public async Task ByDate_Anonymous_Returns401()
    {
        var response = await CreateUnauthenticatedClient().GetAsync(Url(DateOnly.FromDateTime(DateTime.UtcNow)), CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact(DisplayName = "The day view is reachable by a plain User role — it does not override AllowedRoles()")]
    public async Task ByDate_UserRole_IsAllowed()
    {
        var response = await CreateUserRoleClient().GetAsync(Url(DateOnly.FromDateTime(DateTime.UtcNow)), CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "Roles(this.GetUserRole()) admits User/Admin/Root; a 403 here would mean the gate was narrowed by accident");
    }

    [Fact(DisplayName = "The day view lists only the caller's own reminders")]
    public async Task ByDate_ExcludesOtherUsersReminders()
    {
        var date = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(3);
        var remindAt = date.ToDateTime(new TimeOnly(9, 0));

        long mine;
        await using (var seed = CreateDbContext())
        {
            mine = (await ReminderSeedHelper.SeedReminderAsync(seed, TestUserId, remindAt, "Mine", ct: CancellationToken)).Id;
            await ReminderSeedHelper.EnsureOtherUserAsync(seed, CancellationToken);
            await ReminderSeedHelper.SeedReminderAsync(seed, OtherUserId, remindAt, "Theirs", ct: CancellationToken);
        }

        var response = await CreateClient().GetAsync(Url(date), CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var items = await response.Content.ReadFromJsonAsync<JsonElement[]>(JsonOpts, CancellationToken);
        items!.Select(i => i.GetProperty("id").GetInt64())
            .Should().Contain(mine, "the caller's own reminder must be listed")
            .And.HaveCount(1, "another user's reminder on the same day must never appear in the day view");
    }
}

// =====================================================================================================
// The module's recipient self-service: POST api/reminder-dashboard/{my-upcoming,snooze,dismiss}.
//
// These three are the module endpoints that DO scope by hand — my-upcoming filters on an explicit
// ReminderRecipient row for User.GetId(), and snooze/dismiss both go through
// ReminderOccurrenceActionGuard, which answers a uniform "not a recipient" for a missing definition, a
// resolver-strategy one, and someone else's alike. Nothing pinned any of that until now.
// =====================================================================================================

[Collection("Postgres")]
public class ReminderSelfServiceAuthTests(AppDbContextFixture fixture) : ReminderAuthTestBase(fixture)
{
    [Theory(DisplayName = "Every recipient self-service route rejects an anonymous caller")]
    [InlineData("api/reminder-dashboard/my-upcoming")]
    [InlineData("api/reminder-dashboard/snooze")]
    [InlineData("api/reminder-dashboard/dismiss")]
    public async Task SelfService_Anonymous_Returns401(string url)
    {
        var response = await CreateUnauthenticatedClient().PostAsJsonAsync(url, new { }, JsonOpts, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact(DisplayName = "My-upcoming is reachable by a plain User role — it is the non-admin view by design")]
    public async Task MyUpcoming_UserRole_IsAllowed()
    {
        var response = await CreateUserRoleClient().PostAsJsonAsync("api/reminder-dashboard/my-upcoming", UnfilteredPage(), JsonOpts, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact(DisplayName = "My-upcoming returns only definitions the caller is an explicit recipient of")]
    public async Task MyUpcoming_ExcludesOtherUsersDefinitions()
    {
        var mine = await CreateReminderAsAsync(TestUserId, "Mine");
        var theirs = await CreateReminderAsAsync(OtherUserId, "Theirs");

        var response = await CreateClient().PostAsJsonAsync("api/reminder-dashboard/my-upcoming", UnfilteredPage(), JsonOpts, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var ids = GridIds(await response.Content.ReadFromJsonAsync<JsonElement>(JsonOpts, CancellationToken));

        ids.Should().Contain(mine.DefinitionId, "the caller is the explicit recipient of their own reminder")
            .And.NotContain(theirs.DefinitionId,
                "ReminderDefinition carries no user query filter — the Recipients.Any(r => r.UserId == caller) clause is the only thing scoping this read");
    }

    [Fact(DisplayName = "The RecipientUserId filter cannot be used to read someone else's upcoming reminders")]
    public async Task MyUpcoming_RecipientFilter_CannotWidenTheScope()
    {
        var theirs = await CreateReminderAsAsync(OtherUserId, "Theirs");

        // The filter is shared with the unscoped admin view (ReminderDashboardQueries.ApplyUpcomingFilter), so
        // a caller can name any user id in it. It must narrow the self-scoped set, never replace it.
        var response = await CreateClient().PostAsJsonAsync("api/reminder-dashboard/my-upcoming", new
        {
            useFilter = true,
            filter = new { recipientUserId = OtherUserId },
            sortBy = Array.Empty<object>(),
            itemsPerPage = 100,
            page = 1
        }, JsonOpts, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        GridIds(await response.Content.ReadFromJsonAsync<JsonElement>(JsonOpts, CancellationToken))
            .Should().NotContain(theirs.DefinitionId);
    }

    [Fact(DisplayName = "Snoozing another user's occurrence 404s and writes no action row")]
    public async Task Snooze_OtherUsersDefinition_Returns404()
    {
        var theirs = await CreateReminderAsAsync(OtherUserId, "Theirs");

        var response = await CreateClient().PostAsJsonAsync("api/reminder-dashboard/snooze", new
        {
            ReminderDefinitionId = theirs.DefinitionId,
            OccurrenceAt = theirs.NextOccurrenceAt,
            SnoozeUntil = DateTimeOffset.UtcNow.AddDays(3)
        }, JsonOpts, CancellationToken);

        // 404, not 403: the guard deliberately gives the same answer for "no such definition" and "not yours",
        // so the response never confirms that a definition id is real.
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);

        await using var db = CreateDbContext();
        (await db.Set<ReminderOccurrenceAction>().CountAsync(CancellationToken))
            .Should().Be(0, "a rejected snooze must not leave an append-only row behind");
    }

    [Fact(DisplayName = "Dismissing another user's occurrence 404s and writes no action row")]
    public async Task Dismiss_OtherUsersDefinition_Returns404()
    {
        var theirs = await CreateReminderAsAsync(OtherUserId, "Theirs");

        var response = await CreateClient().PostAsJsonAsync("api/reminder-dashboard/dismiss", new
        {
            ReminderDefinitionId = theirs.DefinitionId,
            OccurrenceAt = theirs.NextOccurrenceAt
        }, JsonOpts, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);

        await using var db = CreateDbContext();
        (await db.Set<ReminderOccurrenceAction>().CountAsync(CancellationToken)).Should().Be(0);
    }

    [Fact(DisplayName = "A recipient can dismiss their own occurrence — the guard is not simply refusing everything")]
    public async Task Dismiss_OwnOccurrence_Succeeds()
    {
        // The negative cases above would pass just as well against an endpoint that 404s unconditionally. This
        // is the control that proves the 404s are the ownership check firing and nothing else.
        var mine = await CreateReminderAsAsync(TestUserId, "Mine");

        var response = await CreateClient().PostAsJsonAsync("api/reminder-dashboard/dismiss", new
        {
            ReminderDefinitionId = mine.DefinitionId,
            OccurrenceAt = mine.NextOccurrenceAt
        }, JsonOpts, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        await using var db = CreateDbContext();
        var action = await db.Set<ReminderOccurrenceAction>().SingleAsync(CancellationToken);
        action.UserId.Should().Be(TestUserId);
        action.ReminderDefinitionId.Should().Be(mine.DefinitionId);
    }
}

// =====================================================================================================
// api/reminder-preference — the caller's per-kind opt-outs.
//
// Both routes read and write ReminderKindPreference by User.GetId() only; the entity has no user query
// filter behind it, so those two WHERE clauses are the whole boundary.
// =====================================================================================================

[Collection("Postgres")]
public class ReminderPreferenceAuthTests(AppDbContextFixture fixture) : ReminderAuthTestBase(fixture)
{
    private static object PreferenceBody(bool enabled) => new
    {
        OwnerModule = ReminderRegistrationService.OwnerModule,
        Kind = ReminderRegistrationService.Kind,
        Enabled = enabled
    };

    [Fact(DisplayName = "Reading reminder preferences rejects an anonymous caller")]
    public async Task GetPreferences_Anonymous_Returns401()
    {
        var response = await CreateUnauthenticatedClient().GetAsync("api/reminder-preference", CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact(DisplayName = "Updating a reminder preference rejects an anonymous caller")]
    public async Task UpdatePreference_Anonymous_Returns401()
    {
        var response = await CreateUnauthenticatedClient()
            .PutAsJsonAsync("api/reminder-preference/kind", PreferenceBody(false), JsonOpts, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact(DisplayName = "Both preference routes are reachable by a plain User role")]
    public async Task Preferences_UserRole_IsAllowed()
    {
        var client = CreateUserRoleClient();

        (await client.PutAsJsonAsync("api/reminder-preference/kind", PreferenceBody(false), JsonOpts, CancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await client.GetAsync("api/reminder-preference", CancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact(DisplayName = "One user's opt-out is invisible to another, and updating never touches their row")]
    public async Task Preferences_AreSelfScoped()
    {
        await using (var seed = CreateDbContext())
            await ReminderSeedHelper.EnsureOtherUserAsync(seed, CancellationToken);

        // The other user mutes personal reminders.
        await using (var otherFactory = CreateFactory(TestRoles.User, OtherUserId))
            (await otherFactory.CreateClient().PutAsJsonAsync("api/reminder-preference/kind", PreferenceBody(false), JsonOpts, CancellationToken))
                .StatusCode.Should().Be(HttpStatusCode.NoContent);

        var response = await CreateClient().GetAsync("api/reminder-preference", CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var payload = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOpts, CancellationToken);
        payload.GetProperty("kindPreferences").GetArrayLength()
            .Should().Be(0, "the caller set no preference of their own; the other user's opt-out is not theirs to see");

        // And the caller's own opt-in must upsert a second row rather than overwrite the other user's — the
        // unique index is (UserId, OwnerModule, Kind), so a missed UserId in the lookup would silently
        // hijack whichever row happened to match on the other two.
        (await CreateClient().PutAsJsonAsync("api/reminder-preference/kind", PreferenceBody(true), JsonOpts, CancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        await using var db = CreateDbContext();
        var rows = await db.Set<ReminderKindPreference>().ToListAsync(CancellationToken);
        rows.Should().HaveCount(2);
        rows.Single(p => p.UserId == OtherUserId).Enabled.Should().BeFalse("the other user's opt-out must survive untouched");
        rows.Single(p => p.UserId == TestUserId).Enabled.Should().BeTrue();
    }
}

// =====================================================================================================
// api/reminder-definition/* and the cross-module dashboard reads — the deliberately unscoped surface.
//
// These nine routes are the module's admin inspector and manual-ops surface. They are unscoped by design:
// ReminderDefinition is BaseTableEntity (no global user query filter), ReminderDefinitionGridEndpoint's
// ApplyUserScoping is the base no-op, GetReminderByIdEndpoint's AuthorizeAsync is a flat true, and the
// four command routes hand a client-supplied ReminderKey straight to a registry that has no notion of a
// caller. The role gate is therefore the ONLY boundary in front of every one of them — which is exactly
// what these tests exist to hold in place.
//
// All nine sat at Roles(this.GetUserRole()) until this suite was written, which contradicted the module's
// own summary.md ("Command endpoints … Admin/RootAdmin", "Both Admin/RootAdmin only (infra views)", and
// the phase-05 dashboard table listing Admin for all five reads) as well as ReminderRegistrationService's
// class comment. The code had drifted from its spec, not the other way round; they are now GetAdminRole().
//
// So each test below asserts the pair that makes the gate load-bearing: a plain User is refused, and an
// Admin still gets the cross-user view the endpoint exists to provide. Asserting only the 403 would pass
// just as well against an endpoint that had been broken outright.
// =====================================================================================================

[Collection("Postgres")]
public class ReminderDefinitionAdminOnlyTests(AppDbContextFixture fixture) : ReminderAuthTestBase(fixture)
{
    [Theory(DisplayName = "Every reminder-definition and admin-dashboard route rejects an anonymous caller")]
    [InlineData("api/reminder-definition/register")]
    [InlineData("api/reminder-definition/pause")]
    [InlineData("api/reminder-definition/resume")]
    [InlineData("api/reminder-definition/cancel")]
    [InlineData("api/reminder-definition/filtered-table")]
    [InlineData("api/reminder-dashboard/upcoming")]
    [InlineData("api/reminder-dashboard/upcoming/export")]
    [InlineData("api/reminder-dashboard/dispatch-history")]
    [InlineData("api/reminder-dashboard/dispatch-history/export")]
    public async Task ModuleRoute_Anonymous_Returns401(string url)
    {
        var response = await CreateUnauthenticatedClient().PostAsJsonAsync(url, new { }, JsonOpts, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Theory(DisplayName = "The two GET module routes reject an anonymous caller")]
    [InlineData("api/reminder-dashboard/overview")]
    [InlineData("api/reminder-definition/1")]
    public async Task ModuleGetRoute_Anonymous_Returns401(string url)
    {
        var response = await CreateUnauthenticatedClient().GetAsync(url, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Theory(DisplayName = "Every unscoped module route refuses a plain User role")]
    [InlineData("api/reminder-definition/register")]
    [InlineData("api/reminder-definition/pause")]
    [InlineData("api/reminder-definition/resume")]
    [InlineData("api/reminder-definition/cancel")]
    [InlineData("api/reminder-definition/filtered-table")]
    [InlineData("api/reminder-dashboard/upcoming")]
    [InlineData("api/reminder-dashboard/upcoming/export")]
    [InlineData("api/reminder-dashboard/dispatch-history")]
    [InlineData("api/reminder-dashboard/dispatch-history/export")]
    public async Task ModuleRoute_UserRole_Returns403(string url)
    {
        var response = await CreateUserRoleClient().PostAsJsonAsync(url, new { }, JsonOpts, CancellationToken);

        // 403 before the body is ever bound, which is why a bare {} suffices here — the gate is the assertion.
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "these routes are unscoped by design, so the role gate is the only thing standing in front of them");
    }

    [Fact(DisplayName = "The overview roll-up refuses a plain User role")]
    public async Task Overview_UserRole_Returns403()
    {
        var response = await CreateUserRoleClient().GetAsync("api/reminder-dashboard/overview", CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact(DisplayName = "An Admin still sees every user's upcoming reminders — the cross-user view is the point of the endpoint")]
    public async Task UpcomingDashboard_Admin_SpansAllUsers()
    {
        var mine = await CreateReminderAsAsync(TestUserId, "Mine");
        var theirs = await CreateReminderAsAsync(OtherUserId, "Theirs");

        var response = await CreateAdminRoleClient().PostAsJsonAsync("api/reminder-dashboard/upcoming", UnfilteredPage(), JsonOpts, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        GridIds(await response.Content.ReadFromJsonAsync<JsonElement>(JsonOpts, CancellationToken))
            .Should().Contain(mine.DefinitionId)
            .And.Contain(theirs.DefinitionId,
                "narrowing the gate must not have been done by bolting on scoping — the self-service view is GetMyUpcomingRemindersEndpoint");
    }

    [Fact(DisplayName = "A plain User cannot read another user's reminder definition by id; an Admin can")]
    public async Task GetDefinitionById_IsAdminOnly()
    {
        var theirs = await CreateReminderAsAsync(OtherUserId, "Theirs");
        var url = $"api/reminder-definition/{theirs.DefinitionId}";

        // The DTO carries the recipient list and the owner-supplied payload document, and AuthorizeAsync is a
        // flat true, so a User-tier gate here handed those over to anyone who could guess a sequence id.
        (await CreateUserRoleClient().GetAsync(url, CancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await CreateAdminRoleClient().GetAsync(url, CancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact(DisplayName = "A plain User cannot pause another user's reminder by its key, and the reminder stays active")]
    public async Task PauseByKey_UserRole_IsRefusedAndChangesNothing()
    {
        var theirs = await CreateReminderAsAsync(OtherUserId, "Theirs");

        // The key is neither a secret nor a capability: it is (Portal, Reminder, <portal reminder id>,
        // PersonalReminder) and the id is a sequence value, so the whole key space is enumerable. The registry
        // cancels/pauses by key without consulting Recipients, so nothing downstream of the gate would have
        // caught this — hence the row assertion rather than a status-code-only test.
        var response = await CreateUserRoleClient()
            .PostAsJsonAsync("api/reminder-definition/pause", KeyRequestFor(theirs.ReminderId), JsonOpts, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        await using var db = CreateDbContext();
        var definition = await DefinitionForAsync(db, theirs.ReminderId);
        definition.Status.Should().Be(ReminderStatus.Active, "a refused pause must not have reached the registry");
        definition.NextOccurrenceAt.Should().NotBeNull("the owner's reminder is still scheduled to fire");
    }

    [Fact(DisplayName = "A plain User cannot cancel another user's reminder by its key, and the reminder stays active")]
    public async Task CancelByKey_UserRole_IsRefusedAndChangesNothing()
    {
        var theirs = await CreateReminderAsAsync(OtherUserId, "Theirs");

        var response = await CreateUserRoleClient()
            .PostAsJsonAsync("api/reminder-definition/cancel", KeyRequestFor(theirs.ReminderId), JsonOpts, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        await using var db = CreateDbContext();
        var definition = await DefinitionForAsync(db, theirs.ReminderId);
        definition.Status.Should().Be(ReminderStatus.Active);

        // What made this the sharpest of the gaps: a cancel leaves the owner's portal row untouched, so their
        // own view would still show a reminder that had been silently withdrawn and would never fire again.
        (await db.Set<Planning.domain.model.entity.reminder.Reminder>().IgnoreQueryFilters()
            .CountAsync(r => r.Id == theirs.ReminderId, CancellationToken)).Should().Be(1);
    }

    [Fact(DisplayName = "An Admin can still pause a registered reminder by key — the manual-ops path survives the gate")]
    public async Task PauseByKey_Admin_StillWorks()
    {
        var mine = await CreateReminderAsAsync(TestUserId, "Mine");

        var response = await CreateAdminRoleClient()
            .PostAsJsonAsync("api/reminder-definition/pause", KeyRequestFor(mine.ReminderId), JsonOpts, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        await using var db = CreateDbContext();
        var definition = await DefinitionForAsync(db, mine.ReminderId);
        definition.Status.Should().Be(ReminderStatus.Paused);
        definition.NextOccurrenceAt.Should().BeNull();
    }
}
