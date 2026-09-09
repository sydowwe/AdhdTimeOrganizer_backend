using AdhdTimeOrganizer.Core.infrastructure.persistence;
using System.Net;
using System.Net.Http.Json;
using AdhdTimeOrganizer.ActivityProfiles.domain.model.entity;
using AdhdTimeOrganizer.ActivityProfiles.domain.model.@enum;
using AdhdTimeOrganizer.Core.domain.model.entity.@base;
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
/// `TEST-4` / `SEC-4` — the consequences of the archival filter inside the reference seam, which the
/// count-shaped assertions elsewhere do not spell out.
/// </summary>
/// <remarks>
/// <para>
/// Three of the tables <c>IActivityReferenceSource</c> reads (the three <c>Activity*Profile</c> families)
/// mirror <c>AppQueryFilters.ActivityArchived</c>, so a source that does not lift it reports
/// <c>usageCount: 0</c> for an archived activity whose profile rows exist.
/// <c>ActivityProfileActivityReferenceSource</c> lifts it; these tests pin the two things that a count
/// assertion on its own cannot say.
/// </para>
/// <list type="bullet">
/// <item><b>What the wrong count costs.</b> <c>canDelete</c> is the <em>only</em> thing withholding the
/// archive view's delete row action, and <c>DeleteActivityEndpoint</c> lifts the archival filter on
/// purpose so that action works — so an over-optimistic count is not a cosmetic number, it is an offered
/// button that cascades the profile away.</item>
/// <item><b>That lifting the filter did not start double-counting.</b> The filter was never hiding
/// anything on an <em>active</em> activity, so no count there may move — and the archived activity's
/// unfiltered sources (history, timer presets) must still be counted alongside the profile rather than
/// replaced by them.</item>
/// </list>
/// <para>
/// The count itself is asserted next door: <c>ActivityArchivingTests.UsageCount_CountsTheProfilesOfAnArchivedActivity</c>
/// covers both read paths (<c>GET /activity/{id}</c> groups, the grid correlates a subquery) and
/// <c>ActivityMergeTests.Merge_IntoAnArchivedSurvivorThatAlreadyHasAProfile_KeepsTheSurvivorsProfile</c>
/// covers the unique-index collision. This file does not restate them.
/// </para>
/// </remarks>
[Collection("Postgres")]
public class ArchivedActivityReferenceCountTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    private static long UserId => FakeLoggedUserService.TestUserId;

    private sealed record ActivityRow(long Id, string Name, bool IsArchived, int UsageCount, bool CanDelete);

    private sealed record GridPage(List<ActivityRow> Items, int TotalItems);

    /// <summary>
    /// The consequence, spelled out so the rule cannot be argued down to "a wrong number on screen": with
    /// the count correct the button is withheld, and the delete it was withholding really would destroy
    /// the profile.
    /// </summary>
    /// <remarks>
    /// The delete half is asserted on purpose rather than left implied. <c>DELETE /activity/{id}</c>
    /// deliberately lifts the archival filter (<c>DeleteActivityEndpoint.IgnoredQueryFilters</c>) and every
    /// FK into <c>Activity</c> bar one is <c>Cascade</c>, so nothing at the database or the endpoint stops
    /// it — <c>canDelete</c> is the whole guard, and this pins what it is guarding.
    /// </remarks>
    [Fact(DisplayName = "SEC-4: an archived activity holding a profile is not offered as deletable, and the delete it withholds would destroy the profile")]
    public async Task ArchivedActivityWithAProfile_IsNotDeletable_AndTheDeleteWouldCascadeTheProfileAway()
    {
        await using var db = CreateDbContext();
        var activityId = await SeedActivityAsync(db, "Archive Then Delete");
        var profileId = await SeedBacklogProfileAsync(db, activityId);

        // Through the endpoint rather than a direct write, so the real state transition is exercised.
        var archive = await CreateClient().PatchAsJsonAsync($"api/activity/{activityId}/archived",
            new { IsArchived = true }, JsonOpts, TestContext.Current.CancellationToken);
        archive.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var row = await CreateClient().GetFromJsonAsync<ActivityRow>($"api/activity/{activityId}", JsonOpts, CancellationToken);

        row!.IsArchived.Should().BeTrue();
        row.UsageCount.Should().BeGreaterThanOrEqualTo(1, "the profile row exists whether or not the activity is archived");
        row.CanDelete.Should().BeFalse("canDelete is the only thing withholding the archive view's delete row action");

        var delete = await CreateClient().DeleteAsync($"api/activity/{activityId}", CancellationToken);
        delete.StatusCode.Should().Be(HttpStatusCode.NoContent, "the route lifts the archival filter on purpose, so it succeeds regardless");

        await using var assertDb = CreateDbContext();
        (await assertDb.Set<ActivityBacklogProfile>()
                .IgnoreQueryFilters()
                .AnyAsync(p => p.Id == profileId, CancellationToken))
            .Should().BeFalse("the activity FK is Cascade — this is exactly the row canDelete: false was protecting");
    }

    /// <summary>
    /// The regression pin for the fix itself: lifting the archival filter inside the seam must not change
    /// any count on an active activity, because the filter was never hiding anything there.
    /// </summary>
    /// <remarks>
    /// A profile counted twice is the plausible way to over-correct — <c>IgnoreQueryFilters</c> applies
    /// across the whole compiled tree, so a second, unlifted read of the same table concatenated into the
    /// union would not fail, it would just report one profile as two and disable a delete the user is
    /// entitled to. Both read paths are checked because they build the count differently.
    /// </remarks>
    [Fact(DisplayName = "SEC-4: an active activity's profile is still counted exactly once")]
    public async Task ActiveActivityWithAProfile_CountsItExactlyOnce()
    {
        await using var db = CreateDbContext();
        var activityId = await SeedActivityAsync(db, "Active And Profiled");
        await SeedBacklogProfileAsync(db, activityId);

        var row = await CreateClient().GetFromJsonAsync<ActivityRow>($"api/activity/{activityId}", JsonOpts, CancellationToken);

        row!.UsageCount.Should().Be(1, "one profile is one reference — lifting a filter that hid nothing must not double it");
        row.CanDelete.Should().BeFalse();

        var page = await FetchTableAsync(new { IsArchived = (bool?)false });
        var gridRow = page.Items.Single(i => i.Id == activityId);

        gridRow.UsageCount.Should().Be(1, "the grid's correlated subquery is a second code path over the same seam");
        gridRow.CanDelete.Should().BeFalse();
    }

    /// <summary>
    /// The archived activity's <em>other</em> references still count. Only the profile tables carry the
    /// archival filter, so a source that lifted it for everything — or a fix that swapped the union for a
    /// profiles-only read — would show up here as a count of one instead of three.
    /// </summary>
    [Fact(DisplayName = "SEC-4: an archived activity counts its filtered and unfiltered references together")]
    public async Task ArchivedActivity_CountsProfilesAlongsideItsUnfilteredReferences()
    {
        await using var db = CreateDbContext();
        var activityId = await SeedActivityAsync(db, "Archived And Busy", isArchived: true);
        await SeedBacklogProfileAsync(db, activityId);

        db.Set<TimerPreset>().Add(new TimerPreset { UserId = UserId, Duration = 600, ActivityId = activityId });
        db.Set<ActivityHistory>().Add(new ActivityHistory
        {
            UserId = UserId,
            ActivityId = activityId,
            StartTimestamp = DateTime.UtcNow.AddHours(-3),
            Length = new IntTime(0, 15),
            EndTimestamp = DateTime.UtcNow.AddMinutes(-165)
        });
        await db.SaveChangesAsync(CancellationToken);

        var row = await CreateClient().GetFromJsonAsync<ActivityRow>($"api/activity/{activityId}", JsonOpts, CancellationToken);

        row!.UsageCount.Should().Be(3,
            "one profile (ActivityProfiles, archival-filtered) plus one timer preset (Core) plus one history row (History), all through the one seam");
        row.CanDelete.Should().BeFalse();

        var page = await FetchTableAsync(new { IsArchived = (bool?)null });
        page.Items.Single(i => i.Id == activityId).UsageCount.Should().Be(3);
    }

    // ---- seeding ---------------------------------------------------------------------------------

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

    private async Task<long> SeedActivityAsync(DbContext db, string name, bool isArchived = false)
    {
        var role = new ActivityRole { UserId = UserId, Name = $"{name} Role {Guid.NewGuid():N}", Color = "#112233" };
        db.Set<ActivityRole>().Add(role);
        await db.SaveChangesAsync(CancellationToken);

        // The container is shared across the collection and (UserId, Name) is unique.
        var activity = new Activity { UserId = UserId, Name = $"{name} {Guid.NewGuid():N}", RoleId = role.Id, IsArchived = isArchived };
        db.Set<Activity>().Add(activity);
        await db.SaveChangesAsync(CancellationToken);
        return activity.Id;
    }

    private async Task<long> SeedBacklogProfileAsync(DbContext db, long activityId)
    {
        var profile = new ActivityBacklogProfile
        {
            ActivityId = activityId,
            LocationTypeId = await SeedLookupAsync<ActivityLocationType>(db),
            WeatherDependencyId = await SeedLookupAsync<ActivityWeatherDependency>(db),
            ExpectedCostTierId = await SeedLookupAsync<ActivityExpectedCostTier>(db),
            EnergyLevel = EnergyLevel.Medium,
            MinParticipants = 1,
            DurationMinutes = 30,
            IsRepeatable = true
        };
        db.Set<ActivityBacklogProfile>().Add(profile);
        await db.SaveChangesAsync(CancellationToken);
        return profile.Id;
    }

    private async Task<long> SeedLookupAsync<TLookup>(DbContext db) where TLookup : BaseLookupWithUser, new()
    {
        var lookup = new TLookup { UserId = UserId, Text = $"{typeof(TLookup).Name}-{Guid.NewGuid():N}" };
        db.Set<TLookup>().Add(lookup);
        await db.SaveChangesAsync(CancellationToken);
        return lookup.Id;
    }
}
