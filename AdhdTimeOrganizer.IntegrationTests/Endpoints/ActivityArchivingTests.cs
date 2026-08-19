using System.Net;
using System.Net.Http.Json;
using AdhdTimeOrganizer.Core.domain.model.entity.activity;
using AdhdTimeOrganizer.Core.domain.model.entity.timer;
using AdhdTimeOrganizer.History.domain.model.entity.activityHistory;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.domain.valueObject;
using Sydowwe.Framework.Testing;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Endpoints;

/// <summary>
/// A9, archive half — <c>PATCH /activity/{id}/archived</c>, the <c>isArchived</c> filter, and the
/// question that actually decides whether the feature works: <b>which endpoints exclude archived rows
/// and which must not</b>.
/// </summary>
/// <remarks>
/// <para>
/// The rule is one line — <em>only pickers exclude archived activities</em> — and it is easy to half-do
/// in both directions. An archived activity leaking back into one dropdown makes the whole feature
/// pointless and the client cannot patch over it; an archived activity vanishing from a history row or
/// a planner task makes existing records unreadable. Neither failure raises anything: the response is
/// a valid 200 with a list of the wrong length.
/// </para>
/// <para>
/// So these assert on <b>returned rows</b>, not on routes. Every picker gets a seeded archived activity
/// and is asserted not to contain it; every record-reading surface gets the same activity and is
/// asserted to still resolve it.
/// </para>
/// </remarks>
[Collection("Postgres")]
public class ActivityArchivingTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    private static long UserId => FakeLoggedUserService.TestUserId;

    private async Task<long> SeedActivityAsync(DbContext db, string name, bool isArchived = false)
    {
        var role = new ActivityRole { UserId = UserId, Name = $"{name} Role {Guid.NewGuid():N}", Color = "#112233" };
        db.Set<ActivityRole>().Add(role);
        await db.SaveChangesAsync(CancellationToken);

        var activity = new Activity { UserId = UserId, Name = name, RoleId = role.Id, IsArchived = isArchived };
        db.Set<Activity>().Add(activity);
        await db.SaveChangesAsync(CancellationToken);
        return activity.Id;
    }

    private sealed record ActivityRow(long Id, string Name, bool IsArchived, int UsageCount, bool CanDelete);

    private sealed record GridPage(List<ActivityRow> Items, int TotalItems);

    private sealed record SelectOption(long Id, string Text);

    private async Task<GridPage> FetchTableAsync(object? filter)
    {
        var response = await CreateClient().PostAsJsonAsync("api/activity/filtered-table", new
        {
            Page = 1,
            ItemsPerPage = 100,
            SortBy = Array.Empty<object>(),
            UseFilter = filter != null,
            Filter = filter
        }, JsonOpts);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<GridPage>(JsonOpts, CancellationToken))!;
    }

    // ---- PATCH /activity/{id}/archived ----------------------------------------------------------

    [Fact]
    public async Task Archive_ThenRestore_RoundTripsTheFlag()
    {
        await using var db = CreateDbContext();
        var activityId = await SeedActivityAsync(db, "Round Trip");

        var archive = await CreateClient().PatchAsJsonAsync($"api/activity/{activityId}/archived", new { IsArchived = true }, JsonOpts);
        archive.StatusCode.Should().Be(HttpStatusCode.NoContent);

        await using (var assertDb = CreateDbContext())
            (await assertDb.Set<Activity>().IgnoreQueryFilters().FirstAsync(a => a.Id == activityId, CancellationToken))
                .IsArchived.Should().BeTrue();

        var restore = await CreateClient().PatchAsJsonAsync($"api/activity/{activityId}/archived", new { IsArchived = false }, JsonOpts);
        restore.StatusCode.Should().Be(HttpStatusCode.NoContent);

        await using var finalDb = CreateDbContext();
        (await finalDb.Set<Activity>().IgnoreQueryFilters().FirstAsync(a => a.Id == activityId, CancellationToken))
            .IsArchived.Should().BeFalse("one endpoint serves both directions, so restore is the same handler with a different body");
    }

    /// <summary>
    /// Archiving an already-archived activity is a success, not a conflict. The row action can be
    /// double-clicked and there is no useful error to show for it.
    /// </summary>
    [Fact]
    public async Task Archive_IsIdempotent()
    {
        await using var db = CreateDbContext();
        var activityId = await SeedActivityAsync(db, "Idempotent", isArchived: true);

        var response = await CreateClient().PatchAsJsonAsync($"api/activity/{activityId}/archived", new { IsArchived = true }, JsonOpts);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent, "re-archiving must not 409");
    }

    [Fact]
    public async Task Archive_UnknownId_Returns404()
    {
        var response = await CreateClient().PatchAsJsonAsync("api/activity/987654321/archived", new { IsArchived = true }, JsonOpts);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// Another user's activity answers 404, not 403 — the global query filter removes it before the
    /// handler sees it, so the response never confirms that the id is real.
    /// </summary>
    [Fact]
    public async Task Archive_OtherUsersActivity_Returns404()
    {
        await using var db = CreateDbContext();
        var otherUserId = await SecondUserAsync($"archive-{Guid.NewGuid():N}@test.com");

        var role = new ActivityRole { UserId = otherUserId, Name = $"Foreign Role {Guid.NewGuid():N}", Color = "#112233" };
        db.Set<ActivityRole>().Add(role);
        await db.SaveChangesAsync(CancellationToken);
        var foreign = new Activity { UserId = otherUserId, Name = "Foreign Activity", RoleId = role.Id };
        db.Set<Activity>().Add(foreign);
        await db.SaveChangesAsync(CancellationToken);

        var response = await CreateClient().PatchAsJsonAsync($"api/activity/{foreign.Id}/archived", new { IsArchived = true }, JsonOpts);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);

        await using var assertDb = CreateDbContext();
        (await assertDb.Set<Activity>().IgnoreQueryFilters().FirstAsync(a => a.Id == foreign.Id, CancellationToken))
            .IsArchived.Should().BeFalse("the foreign row must be untouched, not merely unreported");
    }

    /// <summary>
    /// <c>PUT /activity/{id}</c> must never move the flag. <c>ActivityRequest</c> does not carry it, so a
    /// mapping that assigned it would silently un-archive on every edit — the trap
    /// <c>ActivityHistoryRequest</c> documents for its two item links.
    /// </summary>
    [Fact]
    public async Task Update_DoesNotUnarchive()
    {
        await using var db = CreateDbContext();
        var activityId = await SeedActivityAsync(db, "Edited While Archived", isArchived: true);
        var roleId = (await db.Set<Activity>().FirstAsync(a => a.Id == activityId, CancellationToken)).RoleId;

        var response = await CreateClient().PutAsJsonAsync($"api/activity/{activityId}",
            new { Name = "Edited While Archived", IsUnavoidable = true, RoleId = roleId }, JsonOpts);

        response.IsSuccessStatusCode.Should().BeTrue();

        await using var assertDb = CreateDbContext();
        var reloaded = await assertDb.Set<Activity>().IgnoreQueryFilters().FirstAsync(a => a.Id == activityId, CancellationToken);
        reloaded.IsUnavoidable.Should().BeTrue("the edit itself must still apply");
        reloaded.IsArchived.Should().BeTrue("archiving is not part of the edit form");
    }

    // ---- POST /activity/filtered-table ----------------------------------------------------------

    /// <summary>
    /// The rule that lets the settings table's default view keep sending exactly the request it sent
    /// before A9: no filter object at all must behave as <c>isArchived: false</c>.
    /// </summary>
    [Fact]
    public async Task FilteredTable_WithNoFilterObject_ExcludesArchived()
    {
        await using var db = CreateDbContext();
        var activeId = await SeedActivityAsync(db, "Default View Active");
        var archivedId = await SeedActivityAsync(db, "Default View Archived", isArchived: true);

        var page = await FetchTableAsync(null);

        page.Items.Select(i => i.Id).Should().Contain(activeId);
        page.Items.Select(i => i.Id).Should().NotContain(archivedId,
            "an absent filter means active only — otherwise archived rows reappear in the one view most users never leave");
    }

    [Fact]
    public async Task FilteredTable_IsArchivedFilter_IsTriState()
    {
        await using var db = CreateDbContext();
        var activeId = await SeedActivityAsync(db, "Tri State Active");
        var archivedId = await SeedActivityAsync(db, "Tri State Archived", isArchived: true);

        var activeOnly = await FetchTableAsync(new { IsArchived = (bool?)false });
        activeOnly.Items.Select(i => i.Id).Should().Contain(activeId).And.NotContain(archivedId);

        var archivedOnly = await FetchTableAsync(new { IsArchived = (bool?)true });
        archivedOnly.Items.Select(i => i.Id).Should().Contain(archivedId).And.NotContain(activeId);

        // Null is the only way to see an archived row next to an active one — the merge dialog's All view.
        var both = await FetchTableAsync(new { IsArchived = (bool?)null });
        both.Items.Select(i => i.Id).Should().Contain(activeId).And.Contain(archivedId);
    }

    [Fact]
    public async Task FilteredTable_RowCarriesIsArchived()
    {
        await using var db = CreateDbContext();
        var archivedId = await SeedActivityAsync(db, "Flagged Row", isArchived: true);

        var page = await FetchTableAsync(new { IsArchived = (bool?)true });

        page.Items.Single(i => i.Id == archivedId).IsArchived.Should().BeTrue();
    }

    // ---- which endpoints exclude archived rows ---------------------------------------------------

    /// <summary>
    /// <c>GET /activity/all-options</c> feeds the leisure autocomplete, both timer-preset dialogs and the
    /// store's shared activity list. All pickers; all exclude.
    /// </summary>
    [Fact]
    public async Task AllOptions_ExcludesArchived()
    {
        await using var db = CreateDbContext();
        var activeId = await SeedActivityAsync(db, "Option Active");
        var archivedId = await SeedActivityAsync(db, "Option Archived", isArchived: true);

        var options = await CreateClient().GetFromJsonAsync<List<SelectOption>>("api/activity/all-options", JsonOpts, CancellationToken);

        options!.Select(o => o.Id).Should().Contain(activeId).And.NotContain(archivedId);
    }

    [Fact]
    public async Task ActivityFormSelectOptions_ExcludesArchived()
    {
        await using var db = CreateDbContext();
        var activeId = await SeedActivityAsync(db, "Form Active");
        var archivedId = await SeedActivityAsync(db, "Form Archived", isArchived: true);

        var options = await CreateClient().GetFromJsonAsync<List<SelectOption>>("api/activity/form-select-options", JsonOpts, CancellationToken);

        options!.Select(o => o.Id).Should().Contain(activeId).And.NotContain(archivedId);
    }

    /// <summary>
    /// The history one is the case that forced <c>?includeArchived=</c> to exist:
    /// <c>/activity-history/form-select-options</c> feeds <c>HistoryPanelFilter</c>, which is the filter
    /// over history rather than a form that creates a record. Default excludes so existing call sites are
    /// unchanged; the filter surface opts back in, so archiving an activity does not silently remove the
    /// user's ability to filter their own history by it while the rows stay visible.
    /// </summary>
    [Fact]
    public async Task HistoryFormSelectOptions_ExcludesArchivedByDefault_AndIncludesItOnRequest()
    {
        await using var db = CreateDbContext();
        var archivedId = await SeedActivityAsync(db, "History Filter Archived", isArchived: true);
        db.Set<ActivityHistory>().Add(new ActivityHistory
        {
            UserId = UserId,
            ActivityId = archivedId,
            StartTimestamp = DateTime.UtcNow.AddHours(-1),
            Length = new IntTime(0, 30),
            EndTimestamp = DateTime.UtcNow.AddMinutes(-30)
        });
        await db.SaveChangesAsync(CancellationToken);

        var defaultOptions = await CreateClient()
            .GetFromJsonAsync<List<SelectOption>>("api/activity-history/form-select-options", JsonOpts, CancellationToken);
        defaultOptions!.Select(o => o.Id).Should().NotContain(archivedId);

        var withArchived = await CreateClient()
            .GetFromJsonAsync<List<SelectOption>>("api/activity-history/form-select-options?includeArchived=true", JsonOpts, CancellationToken);
        withArchived!.Select(o => o.Id).Should().Contain(archivedId,
            "the history filter must keep offering an activity whose rows it still displays");
    }

    /// <summary>
    /// <c>GET /activity/{id}</c> is explicitly unaffected — it returns archived activities normally, which
    /// is what lets the edit form and the merge dialog open one.
    /// </summary>
    [Fact]
    public async Task GetById_ReturnsArchivedActivitiesNormally()
    {
        await using var db = CreateDbContext();
        var archivedId = await SeedActivityAsync(db, "Still Fetchable", isArchived: true);

        var row = await CreateClient().GetFromJsonAsync<ActivityRow>($"api/activity/{archivedId}", JsonOpts, CancellationToken);

        row!.Id.Should().Be(archivedId);
        row.IsArchived.Should().BeTrue();
    }

    /// <summary>
    /// Roles and categories are deliberately not archivable, and a role whose activities are all archived
    /// still appears in the role dropdown. That is accepted, not an oversight — they number in the tens.
    /// </summary>
    [Fact]
    public async Task RoleOptions_AreUnaffectedByArchiving()
    {
        await using var db = CreateDbContext();
        var archivedId = await SeedActivityAsync(db, "Lonely Role Activity", isArchived: true);
        var roleId = (await db.Set<Activity>().IgnoreQueryFilters().FirstAsync(a => a.Id == archivedId, CancellationToken)).RoleId;

        var options = await CreateClient().GetFromJsonAsync<List<SelectOption>>("api/activity-role/all-options", JsonOpts, CancellationToken);

        options!.Select(o => o.Id).Should().Contain(roleId);
    }

    /// <summary>
    /// Everything that reads a <em>record</em> keeps resolving the activity and keeps showing its name.
    /// This is the half of the rule that a client-side filter could never have got right.
    /// </summary>
    [Fact]
    public async Task HistoryRows_StillResolveAnArchivedActivity()
    {
        await using var db = CreateDbContext();
        var activityId = await SeedActivityAsync(db, "Archived But Recorded");
        db.Set<ActivityHistory>().Add(new ActivityHistory
        {
            UserId = UserId,
            ActivityId = activityId,
            StartTimestamp = DateTime.UtcNow.AddHours(-2),
            Length = new IntTime(0, 45),
            EndTimestamp = DateTime.UtcNow.AddMinutes(-75)
        });
        await db.SaveChangesAsync(CancellationToken);

        var archive = await CreateClient().PatchAsJsonAsync($"api/activity/{activityId}/archived", new { IsArchived = true }, JsonOpts);
        archive.StatusCode.Should().Be(HttpStatusCode.NoContent);

        await using var assertDb = CreateDbContext();
        var stillThere = await assertDb.Set<ActivityHistory>()
            .Include(h => h.Activity)
            .Where(h => h.ActivityId == activityId)
            .ToListAsync(CancellationToken);

        stillThere.Should().ContainSingle();
        stillThere[0].Activity.Name.Should().Be("Archived But Recorded",
            "archiving keeps every row and keeps rendering the name — it is a flag, not a soft delete");
    }

    // ---- usageCount / canDelete ------------------------------------------------------------------

    /// <summary>
    /// The count spans slices Core cannot see, through the <c>IActivityReferenceSource</c> seam. Asserting
    /// a specific number across three slices is the point: a missing source undercounts silently, and the
    /// visible symptom is an enabled delete button next to a year of history.
    /// </summary>
    [Fact]
    public async Task UsageCount_CountsEveryReferencingSlice_AndCanDeleteFollowsIt()
    {
        await using var db = CreateDbContext();
        var unusedId = await SeedActivityAsync(db, "Never Used");
        var usedId = await SeedActivityAsync(db, "Used Everywhere");

        db.Set<ActivityHistory>().Add(new ActivityHistory
        {
            UserId = UserId,
            ActivityId = usedId,
            StartTimestamp = DateTime.UtcNow.AddHours(-3),
            Length = new IntTime(0, 15),
            EndTimestamp = DateTime.UtcNow.AddMinutes(-165)
        });
        db.Set<TimerPreset>().Add(new TimerPreset { UserId = UserId, Duration = 600, ActivityId = usedId });
        await db.SaveChangesAsync(CancellationToken);

        var page = await FetchTableAsync(new { IsArchived = (bool?)null });

        var used = page.Items.Single(i => i.Id == usedId);
        used.UsageCount.Should().Be(2, "one history row (History) plus one timer preset (Core) — both reached through the seam");
        used.CanDelete.Should().BeFalse();

        var unused = page.Items.Single(i => i.Id == unusedId);
        unused.UsageCount.Should().Be(0);
        unused.CanDelete.Should().BeTrue("nothing references it, so the delete row action is safe to offer");
    }

    /// <summary>
    /// <c>GET /activity/{id}</c> carries the same two fields. It computes them with a grouped count rather
    /// than the grid's correlated subquery, so this is a genuinely different code path.
    /// </summary>
    [Fact]
    public async Task GetById_CarriesUsageCountAndCanDelete()
    {
        await using var db = CreateDbContext();
        var activityId = await SeedActivityAsync(db, "Single Row Count");
        db.Set<TimerPreset>().Add(new TimerPreset { UserId = UserId, Duration = 300, ActivityId = activityId });
        await db.SaveChangesAsync(CancellationToken);

        var row = await CreateClient().GetFromJsonAsync<ActivityRow>($"api/activity/{activityId}", JsonOpts, CancellationToken);

        row!.UsageCount.Should().Be(1);
        row.CanDelete.Should().BeFalse();
    }

    /// <summary>
    /// The settings table declares the column sortable, so <c>sortBy: [{ key: "usageCount" }]</c> arrives.
    /// It must not 400, and — the part that would fail silently — it must actually order by the count.
    /// </summary>
    /// <remarks>
    /// This is why the grid overrides <c>Projection</c> rather than <c>PostProcessItemsAsync</c>:
    /// <c>SortByMany</c> runs on the projected queryable, so a count filled in afterwards would sort every
    /// row on <c>0</c> and return a page that looks ordered and is arbitrary.
    /// </remarks>
    [Fact]
    public async Task UsageCount_IsSortable()
    {
        await using var db = CreateDbContext();
        var noneId = await SeedActivityAsync(db, "Sort Zero");
        var oneId = await SeedActivityAsync(db, "Sort One");
        var twoId = await SeedActivityAsync(db, "Sort Two");

        db.Set<TimerPreset>().Add(new TimerPreset { UserId = UserId, Duration = 60, ActivityId = oneId });
        db.Set<TimerPreset>().Add(new TimerPreset { UserId = UserId, Duration = 60, ActivityId = twoId });
        db.Set<TimerPreset>().Add(new TimerPreset { UserId = UserId, Duration = 120, ActivityId = twoId });
        await db.SaveChangesAsync(CancellationToken);

        var response = await CreateClient().PostAsJsonAsync("api/activity/filtered-table", new
        {
            Page = 1,
            ItemsPerPage = 100,
            // The wire shape is { key, isDesc }, not the { key, order } the A9 ask assumed — that is this
            // solution's existing SortByRequest and it is unchanged by A9. IsDesc is `required`, so an
            // `order` payload does not merely sort wrongly, it fails model binding with a 400.
            SortBy = new[] { new { Key = "usageCount", IsDesc = true } },
            UseFilter = false,
            Filter = (object?)null
        }, JsonOpts);

        response.StatusCode.Should().Be(HttpStatusCode.OK, "the column is declared sortable, so this must not 400");

        var page = (await response.Content.ReadFromJsonAsync<GridPage>(JsonOpts, CancellationToken))!;
        var ordered = page.Items.Where(i => i.Id == noneId || i.Id == oneId || i.Id == twoId).ToList();

        // Descending by usageCount is how a user finds the unused rows worth archiving.
        ordered.Select(i => i.Id).Should().ContainInOrder(twoId, oneId, noneId);
    }

    private async Task<long> SecondUserAsync(string email) => await ActivityMergeTestSupport.SecondUserAsync(Fixture, email);
}
