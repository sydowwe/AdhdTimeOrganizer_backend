using AdhdTimeOrganizer.Core.infrastructure.persistence;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AdhdTimeOrganizer.Core.domain.model.entity.activity;
using AdhdTimeOrganizer.History.domain.model.entity.activityHistory;
using AdhdTimeOrganizer.infrastructure.persistence;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.domain.valueObject;
using Sydowwe.Framework.Testing;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Endpoints;

/// <summary>
/// SEC-1 — what deleting an <see cref="ActivityRole"/> or an <see cref="ActivityCategory"/> takes with
/// it.
/// </summary>
/// <remarks>
/// <para>
/// Both lookups used to cascade into <see cref="Activity"/>, and every activity cascades again through
/// 17 more foreign keys, so <c>DELETE api/activity-category/{id}</c> answered 204 while destroying every
/// activity filed under it plus that activity's history, planner tasks, to-do items, profiles, presets
/// and tracker mappings. Nothing caught it: <c>ActivityRoleDeleteTests</c> and
/// <c>ActivityCategoryDeleteTests</c> delete an <em>unused</em> lookup and were green throughout, and
/// <c>ActivityForeignKeyInventoryTests</c> only looked at foreign keys whose principal is the activity —
/// these two have the activity as the dependent.
/// </para>
/// <para>
/// The rule now: a lookup is a classification, not an owner. A category is optional, so its delete is
/// <c>SetNull</c> — the activity survives, uncategorised. A role is required, so <c>SetNull</c> is not
/// available and <c>DeleteActivityRoleEndpoint</c> refuses with 409 instead. Deleting an <em>unused</em>
/// lookup still works, which is the half that stops the fix over-correcting.
/// </para>
/// <para>
/// Every assertion here reads rows back through a fresh context with <c>.IgnoreQueryFilters()</c>, not
/// the status code — the whole failure mode was a correct-looking status code over a destroyed table.
/// </para>
/// </remarks>
[Collection("Postgres")]
public class ActivityLookupDeleteCascadeTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    private static long UserId => FakeLoggedUserService.TestUserId;

    // ---- category: SetNull ------------------------------------------------------------------------

    [Fact(DisplayName = "SEC-1: deleting a category blanks the classification, it does not destroy the activity")]
    public async Task DeletingACategory_KeepsTheActivityAndItsHistory_AndNullsTheCategory()
    {
        long categoryId, activityId, historyId;
        await using (var db = CreateDbContext())
        {
            categoryId = await SeedCategoryAsync(db, "Deep Work");
            activityId = await SeedActivityAsync(db, "Write the thing", categoryId: categoryId);
            historyId = await SeedHistoryAsync(db, activityId);
        }

        var response = await CreateClient().DeleteAsync($"api/activity-category/{categoryId}", CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        await using var assertDb = CreateDbContext();
        var activity = await assertDb.Set<Activity>().IgnoreQueryFilters()
            .FirstOrDefaultAsync(a => a.Id == activityId, CancellationToken);

        activity.Should().NotBeNull("losing a category is losing a classification, not the thing classified");
        activity!.CategoryId.Should().BeNull("the FK is optional, so SetNull is the available and correct semantics");

        (await assertDb.Set<ActivityHistory>().IgnoreQueryFilters().AnyAsync(h => h.Id == historyId, CancellationToken))
            .Should().BeTrue("the activity survived, so nothing downstream of it had any reason to cascade");

        (await assertDb.Set<ActivityCategory>().IgnoreQueryFilters().AnyAsync(c => c.Id == categoryId, CancellationToken))
            .Should().BeFalse("the category itself is genuinely deleted — that part was never in question");
    }

    /// <summary>
    /// The multi-select in Settings reaches this one, where the affected-row count is largest and the
    /// confirmation dialog says the least.
    /// </summary>
    [Fact(DisplayName = "SEC-1: batch-deleting categories has the same blast radius as deleting one")]
    public async Task BatchDeletingCategories_KeepsEveryActivity()
    {
        long firstCategoryId, secondCategoryId, firstActivityId, secondActivityId;
        await using (var db = CreateDbContext())
        {
            firstCategoryId = await SeedCategoryAsync(db, "Batch A");
            secondCategoryId = await SeedCategoryAsync(db, "Batch B");
            firstActivityId = await SeedActivityAsync(db, "Batch A Activity", categoryId: firstCategoryId);
            secondActivityId = await SeedActivityAsync(db, "Batch B Activity", categoryId: secondCategoryId);
        }

        var response = await CreateClient().PostAsJsonAsync("api/activity-category/batch-delete",
            new { ids = new[] { firstCategoryId, secondCategoryId } }, JsonOpts, CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        await using var assertDb = CreateDbContext();
        var activities = await assertDb.Set<Activity>().IgnoreQueryFilters()
            .Where(a => a.Id == firstActivityId || a.Id == secondActivityId)
            .ToListAsync(CancellationToken);

        activities.Should().HaveCount(2, "a bulk reclassification must not become a bulk deletion");
        activities.Should().OnlyContain(a => a.CategoryId == null);
    }

    /// <summary>
    /// An archived activity is exactly the row a user tidying up categories has forgotten owning, and the
    /// <c>ActivityArchived</c> query filter hides it from every read the endpoint would naturally make.
    /// </summary>
    [Fact(DisplayName = "SEC-1: an archived activity survives its category being deleted too")]
    public async Task DeletingACategory_KeepsArchivedActivities()
    {
        long categoryId, activityId;
        await using (var db = CreateDbContext())
        {
            categoryId = await SeedCategoryAsync(db, "Archived Holder");
            activityId = await SeedActivityAsync(db, "Long Retired", categoryId: categoryId, isArchived: true);
        }

        var response = await CreateClient().DeleteAsync($"api/activity-category/{categoryId}", CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        await using var assertDb = CreateDbContext();
        var activity = await assertDb.Set<Activity>().IgnoreQueryFilters()
            .FirstOrDefaultAsync(a => a.Id == activityId, CancellationToken);

        activity.Should().NotBeNull("archiving keeps rows; a category delete must not be the thing that finally removes them");
        activity!.CategoryId.Should().BeNull();
    }

    // ---- role: 409 --------------------------------------------------------------------------------

    [Fact(DisplayName = "SEC-1: deleting a role that still has activities is refused, not cascaded")]
    public async Task DeletingAUsedRole_Is409_AndDestroysNothing()
    {
        long roleId, firstActivityId, secondActivityId, historyId;
        await using (var db = CreateDbContext())
        {
            roleId = await SeedRoleAsync(db, "Still Used");
            firstActivityId = await SeedActivityAsync(db, "Used One", roleId: roleId);
            secondActivityId = await SeedActivityAsync(db, "Used Two", roleId: roleId);
            historyId = await SeedHistoryAsync(db, firstActivityId);
        }

        var response = await CreateClient().DeleteAsync($"api/activity-role/{roleId}", CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict,
            "RoleId is a required FK so SetNull is unavailable — refusing is the only non-destructive answer, "
            + "and the endpoint already had a 409 path for system roles so the client needs no new branch");

        await using var assertDb = CreateDbContext();
        (await assertDb.Set<ActivityRole>().IgnoreQueryFilters().AnyAsync(r => r.Id == roleId, CancellationToken))
            .Should().BeTrue();
        (await assertDb.Set<Activity>().IgnoreQueryFilters()
                .CountAsync(a => a.Id == firstActivityId || a.Id == secondActivityId, CancellationToken))
            .Should().Be(2);
        (await assertDb.Set<ActivityHistory>().IgnoreQueryFilters().AnyAsync(h => h.Id == historyId, CancellationToken))
            .Should().BeTrue();
    }

    /// <summary>
    /// The count behind the refusal lifts <c>AppQueryFilters.ActivityArchived</c> on purpose. A count that
    /// respected it would answer 204 here and cascade away an activity and a year of history that the user
    /// archived precisely because they wanted to keep it.
    /// </summary>
    [Fact(DisplayName = "SEC-1: an archived activity still blocks its role from being deleted")]
    public async Task DeletingARoleUsedOnlyByAnArchivedActivity_Is409()
    {
        long roleId, activityId;
        await using (var db = CreateDbContext())
        {
            roleId = await SeedRoleAsync(db, "Archived Only");
            activityId = await SeedActivityAsync(db, "Retired But Kept", roleId: roleId, isArchived: true);
        }

        var response = await CreateClient().DeleteAsync($"api/activity-role/{roleId}", CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);

        await using var assertDb = CreateDbContext();
        (await assertDb.Set<Activity>().IgnoreQueryFilters().AnyAsync(a => a.Id == activityId, CancellationToken))
            .Should().BeTrue("the archived row cascades exactly like an active one, so it has to be counted like one");
    }

    // ---- the regression pin: unused lookups stay deletable ----------------------------------------

    /// <summary>
    /// Stops the fix over-correcting into "lookups can never be deleted". No <c>KnownGap</c>: this passed
    /// before the fix and must keep passing after it.
    /// </summary>
    [Fact(DisplayName = "SEC-1: an unused role and an unused category are still deletable")]
    public async Task DeletingAnUnusedLookup_Still204s_AndRemovesTheRow()
    {
        long roleId, categoryId;
        await using (var db = CreateDbContext())
        {
            roleId = await SeedRoleAsync(db, "Unused Role");
            categoryId = await SeedCategoryAsync(db, "Unused Category");
        }

        var client = CreateClient();
        (await client.DeleteAsync($"api/activity-role/{roleId}", CancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await client.DeleteAsync($"api/activity-category/{categoryId}", CancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        await using var assertDb = CreateDbContext();
        (await assertDb.Set<ActivityRole>().IgnoreQueryFilters().AnyAsync(r => r.Id == roleId, CancellationToken))
            .Should().BeFalse();
        (await assertDb.Set<ActivityCategory>().IgnoreQueryFilters().AnyAsync(c => c.Id == categoryId, CancellationToken))
            .Should().BeFalse();
    }

    // ---- usageCount / canDelete on the lookups ----------------------------------------------------

    /// <summary>
    /// Asserted on the raw JSON rather than a typed round-trip: the failure this guards against is the
    /// field being <em>absent</em> from the payload, which a deserializer silently turns into a default.
    /// </summary>
    [Fact(DisplayName = "SEC-1: role and category reads carry usageCount/canDelete like activities do")]
    public async Task LookupReads_CarryUsageCountAndCanDelete()
    {
        long roleId, categoryId, unusedRoleId;
        await using (var db = CreateDbContext())
        {
            roleId = await SeedRoleAsync(db, "Counted Role");
            categoryId = await SeedCategoryAsync(db, "Counted Category");
            unusedRoleId = await SeedRoleAsync(db, "Uncounted Role");
            await SeedActivityAsync(db, "Counted One", roleId: roleId, categoryId: categoryId);
            await SeedActivityAsync(db, "Counted Two", roleId: roleId, categoryId: categoryId, isArchived: true);
        }

        var client = CreateClient();

        AssertUsage(await client.GetStringAsync($"api/activity-role/{roleId}", CancellationToken), 2, false);
        AssertUsage(await client.GetStringAsync($"api/activity-category/{categoryId}", CancellationToken), 2, false);
        AssertUsage(await client.GetStringAsync($"api/activity-role/{unusedRoleId}", CancellationToken), 0, true);
        return;

        static void AssertUsage(string json, int expectedCount, bool expectedCanDelete)
        {
            using var doc = JsonDocument.Parse(json);

            doc.RootElement.TryGetProperty("usageCount", out var usageCount).Should().BeTrue(
                "the usageCount/canDelete contract ActivityResponse publishes has to reach the two lookups "
                + "that can affect the same rows in bulk, or the UI cannot gate the button: {0}", json);
            usageCount.GetInt32().Should().Be(expectedCount, "archived activities count too");

            doc.RootElement.TryGetProperty("canDelete", out var canDelete).Should().BeTrue(json);
            canDelete.GetBoolean().Should().Be(expectedCanDelete);
        }
    }

    /// <summary>
    /// The grids are where the delete button lives, and the field has to arrive from the projection rather
    /// than a post-process so the column stays sortable — the same trap <c>GridActivityEndpoint</c>
    /// documents.
    /// </summary>
    [Fact(DisplayName = "SEC-1: the role and category grids carry usageCount/canDelete too")]
    public async Task LookupGrids_CarryUsageCountAndCanDelete()
    {
        // The names are filtered on rather than paged through: the Postgres container is shared across the
        // whole collection, so page 1 of an unfiltered lookup grid is not reliably where a seeded row is.
        var roleName = $"Grid Role {Guid.NewGuid():N}";
        var categoryName = $"Grid Category {Guid.NewGuid():N}";

        long roleId, categoryId;
        await using (var db = CreateDbContext())
        {
            roleId = await SeedRoleAsync(db, roleName, suffixed: false);
            categoryId = await SeedCategoryAsync(db, categoryName, suffixed: false);
            await SeedActivityAsync(db, "Grid Activity", roleId: roleId, categoryId: categoryId);
        }

        var client = CreateClient();

        await AssertRowAsync("api/activity-role/grid", roleName, roleId);
        await AssertRowAsync("api/activity-category/grid", categoryName, categoryId);
        return;

        async Task AssertRowAsync(string url, string name, long id)
        {
            var response = await client.PostAsJsonAsync(url, new
            {
                Page = 1, ItemsPerPage = 100, SortBy = Array.Empty<object>(),
                UseFilter = true, Filter = new { Name = name }
            }, JsonOpts, CancellationToken);
            response.StatusCode.Should().Be(HttpStatusCode.OK);

            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
            var row = doc.RootElement.GetProperty("items").EnumerateArray()
                .Single(item => item.GetProperty("id").GetInt64() == id);

            row.GetProperty("usageCount").GetInt32().Should().Be(1, "one activity is filed under it: {0}", url);
            row.GetProperty("canDelete").GetBoolean().Should().BeFalse(url);
        }
    }

    // ---- seeding ----------------------------------------------------------------------------------

    /// <summary>
    /// <paramref name="suffixed"/> is off only when the caller needs to know the exact name it seeded (to
    /// filter a grid by it); the caller then owns making it unique. <c>(UserId, Name)</c> is unique on all
    /// three tables and the container is shared across the collection, so nothing here may be a bare
    /// literal.
    /// </summary>
    private async Task<long> SeedRoleAsync(DbContext db, string name, bool suffixed = true)
    {
        var role = new ActivityRole
        {
            UserId = UserId, Name = suffixed ? $"{name} {Guid.NewGuid():N}" : name, Color = "#112233"
        };
        db.Set<ActivityRole>().Add(role);
        await db.SaveChangesAsync(CancellationToken);
        return role.Id;
    }

    private async Task<long> SeedCategoryAsync(DbContext db, string name, bool suffixed = true)
    {
        var category = new ActivityCategory
        {
            UserId = UserId, Name = suffixed ? $"{name} {Guid.NewGuid():N}" : name, Color = "#445566"
        };
        db.Set<ActivityCategory>().Add(category);
        await db.SaveChangesAsync(CancellationToken);
        return category.Id;
    }

    private async Task<long> SeedActivityAsync(
        DbContext db, string name, long? roleId = null, long? categoryId = null, bool isArchived = false)
    {
        var activity = new Activity
        {
            UserId = UserId,
            Name = $"{name} {Guid.NewGuid():N}",
            RoleId = roleId ?? await SeedRoleAsync(db, $"{name} Own Role"),
            CategoryId = categoryId,
            IsArchived = isArchived
        };
        db.Set<Activity>().Add(activity);
        await db.SaveChangesAsync(CancellationToken);
        return activity.Id;
    }

    private async Task<long> SeedHistoryAsync(DbContext db, long activityId)
    {
        var history = new ActivityHistory
        {
            UserId = UserId,
            ActivityId = activityId,
            StartTimestamp = DateTime.UtcNow.AddHours(-2),
            Length = new IntTime(0, 45),
            EndTimestamp = DateTime.UtcNow.AddMinutes(-75)
        };
        db.Set<ActivityHistory>().Add(history);
        await db.SaveChangesAsync(CancellationToken);
        return history.Id;
    }
}