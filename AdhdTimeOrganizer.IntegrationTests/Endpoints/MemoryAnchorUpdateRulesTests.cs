using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AdhdTimeOrganizer.ActivityProfiles.domain.model.entity;
using AdhdTimeOrganizer.ActivityProfiles.domain.model.@enum;
using AdhdTimeOrganizer.Core.domain.model.entity.@base;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.Testing;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Endpoints;

/// <summary>
/// TEST-4 / SEC-3 — <c>PUT /api/memory-anchor/{id}</c> and the two create-time rules it used to walk
/// past. <c>MemoryAnchorRequest.UpdateEntity</c> wrote <c>e.ActivityId</c>, so an edit could repoint an
/// anchor the caller owns onto any activity in the database — past
/// <c>CreateMemoryAnchorValidator</c>'s "the activity must have a backlog or bucket-list profile" and
/// its "at most 2 anchors per activity per month", neither of which has any DB backing.
/// </summary>
/// <remarks>
/// The fix is immutability: which activity an anchor is about is identity, so <c>UpdateEntity</c> leaves
/// the column alone and the rest of the body still applies. The cross-user half is pinned by
/// <see cref="ActivityIdOwnershipTests.UpdateMemoryAnchor_WithForeignActivityId_IsRejected"/>; what this
/// suite adds is the same-user half — an ignored repoint, and the month/year edit that is the only route
/// left over the two-per-month ceiling.
/// </remarks>
[Collection("Postgres")]
public class MemoryAnchorUpdateRulesTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    private const long UserId = FakeLoggedUserService.TestUserId;

    // ---- seeding ---------------------------------------------------------------------------------

    private static async Task<long> SeedLookupAsync<TLookup>(DbContext db, string text)
        where TLookup : BaseLookupWithUser, new()
    {
        var lookup = new TLookup { UserId = UserId, Text = text };
        db.Set<TLookup>().Add(lookup);
        await db.SaveChangesAsync(CancellationToken);
        return lookup.Id;
    }

    /// <summary>An activity that is eligible to be anchored — i.e. one that carries a backlog profile.</summary>
    private static async Task<long> SeedAnchorableActivityAsync(DbContext db, string name)
    {
        var activityId = await PlanningTestSeedHelper.SeedActivityAsync(db, name, UserId, CancellationToken);

        db.Set<ActivityBacklogProfile>().Add(new ActivityBacklogProfile
        {
            ActivityId = activityId,
            LocationTypeId = await SeedLookupAsync<ActivityLocationType>(db, $"{name} Home"),
            WeatherDependencyId = await SeedLookupAsync<ActivityWeatherDependency>(db, $"{name} Indoor"),
            ExpectedCostTierId = await SeedLookupAsync<ActivityExpectedCostTier>(db, $"{name} Free"),
            EnergyLevel = EnergyLevel.Low,
            MinParticipants = 1,
            DurationMinutes = 30,
            IsRepeatable = false
        });
        await db.SaveChangesAsync(CancellationToken);
        return activityId;
    }

    private static async Task<long> SeedAnchorAsync(DbContext db, long activityId, int year, int month, string note = "Original note")
    {
        var anchor = new MemoryAnchor
        {
            UserId = UserId,
            ActivityId = activityId,
            AnchorYear = year,
            AnchorMonth = month,
            HighlightNote = note,
            Rating = 7
        };
        db.Set<MemoryAnchor>().Add(anchor);
        await db.SaveChangesAsync(CancellationToken);
        return anchor.Id;
    }

    private Task<HttpResponseMessage> PutAsync(long anchorId, object body) =>
        CreateUserRoleClient().PutAsJsonAsync($"/api/memory-anchor/{anchorId}", body, JsonOpts, TestContext.Current.CancellationToken);

    private static object Body(long activityId, int year, int month, string note = "Updated note", int rating = 9) =>
        new { activityId, anchorMonth = month, anchorYear = year, highlightNote = note, rating };

    private async Task<MemoryAnchor> ReloadAsync(long anchorId)
    {
        await using var db = CreateDbContext();
        return await db.Set<MemoryAnchor>().AsNoTracking().SingleAsync(a => a.Id == anchorId, CancellationToken);
    }

    // ---- the repoint -----------------------------------------------------------------------------

    /// <summary>
    /// Scenario 3 of the prompt, adjusted for the immutability fix: the repoint is not refused, it is
    /// <b>ignored</b>. A PUT carries the whole resource and <c>activityId</c> is part of it, so a body
    /// echoing a different id of the caller's own is accepted — the column simply does not move, which is
    /// what keeps the "must have a backlog or bucket-list profile" rule a create-time-only concern.
    /// </summary>
    [Fact(DisplayName = "An update naming the caller's other, profile-less activity leaves ActivityId where it was")]
    public async Task UpdateMemoryAnchor_WithOwnOtherActivityId_DoesNotRepoint()
    {
        long anchoredActivityId, bareActivityId, anchorId;
        await using (var db = CreateDbContext())
        {
            anchoredActivityId = await SeedAnchorableActivityAsync(db, "Anchored activity");
            bareActivityId = await PlanningTestSeedHelper.SeedActivityAsync(db, "Bare activity, no profile", UserId, CancellationToken);
            anchorId = await SeedAnchorAsync(db, anchoredActivityId, 2026, 5);
        }

        var response = await PutAsync(anchorId, Body(bareActivityId, 2026, 5));

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var anchor = await ReloadAsync(anchorId);
        anchor.ActivityId.Should().Be(anchoredActivityId, "which activity an anchor is about is identity, not an editable field");
        anchor.HighlightNote.Should().Be("Updated note", "the rest of the body must still apply");
    }

    /// <summary>
    /// Scenario 2. Before the fix this returned 400 off the FK violation while a *real* foreign id
    /// succeeded with a 200 — which is why the assertion that matters is the row, not the status.
    /// </summary>
    [Fact(DisplayName = "An update naming a non-existent activity is refused and changes nothing")]
    public async Task UpdateMemoryAnchor_WithUnknownActivityId_IsRefused()
    {
        long activityId, anchorId;
        await using (var db = CreateDbContext())
        {
            activityId = await SeedAnchorableActivityAsync(db, "Unknown-id activity");
            anchorId = await SeedAnchorAsync(db, activityId, 2026, 5);
        }

        var response = await PutAsync(anchorId, Body(long.MaxValue, 2026, 5));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "the ownership guard answers the same 404 for a foreign id and a missing one, so the route is no existence oracle");

        var anchor = await ReloadAsync(anchorId);
        anchor.ActivityId.Should().Be(activityId);
        anchor.HighlightNote.Should().Be("Original note", "a refused edit must not have been applied");
    }

    // ---- the two-per-month ceiling ---------------------------------------------------------------

    /// <summary>
    /// Scenario 4, in the shape the prompt asks for once <c>ActivityId</c> is immutable: the repoint over
    /// the ceiling is impossible, so the pin moves to the year/month edit, which is the one path left.
    /// </summary>
    [Fact(DisplayName = "Moving an anchor into a month that already holds two is refused")]
    public async Task UpdateMemoryAnchor_IntoAFullMonth_IsRefused()
    {
        long activityId, movingAnchorId;
        await using (var db = CreateDbContext())
        {
            activityId = await SeedAnchorableActivityAsync(db, "Full month activity");
            await SeedAnchorAsync(db, activityId, 2026, 5, "May one");
            await SeedAnchorAsync(db, activityId, 2026, 5, "May two");
            movingAnchorId = await SeedAnchorAsync(db, activityId, 2026, 6, "June one");
        }

        var response = await PutAsync(movingAnchorId, Body(activityId, 2026, 5, "Moved into May"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var anchor = await ReloadAsync(movingAnchorId);
        anchor.AnchorMonth.Should().Be(6, "the refused edit must not have been applied");
        anchor.HighlightNote.Should().Be("June one");
    }

    [Fact(DisplayName = "Moving an anchor into a month that holds one is allowed")]
    public async Task UpdateMemoryAnchor_IntoAMonthWithRoom_IsAllowed()
    {
        long activityId, movingAnchorId;
        await using (var db = CreateDbContext())
        {
            activityId = await SeedAnchorableActivityAsync(db, "Half full month activity");
            await SeedAnchorAsync(db, activityId, 2026, 5, "May one");
            movingAnchorId = await SeedAnchorAsync(db, activityId, 2026, 6, "June one");
        }

        var response = await PutAsync(movingAnchorId, Body(activityId, 2026, 5, "Moved into May"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ReloadAsync(movingAnchorId)).AnchorMonth.Should().Be(5);
    }

    /// <summary>
    /// The check is gated on the period actually changing, so a note-only edit of an anchor that is itself
    /// one of two in its month stays legal — otherwise the ceiling would lock its own occupants out.
    /// </summary>
    [Fact(DisplayName = "A note-only edit of an anchor sitting in a full month still succeeds")]
    public async Task UpdateMemoryAnchor_NoteOnlyInAFullMonth_IsAllowed()
    {
        long activityId, anchorId;
        await using (var db = CreateDbContext())
        {
            activityId = await SeedAnchorableActivityAsync(db, "Note-only activity");
            anchorId = await SeedAnchorAsync(db, activityId, 2026, 5, "May one");
            await SeedAnchorAsync(db, activityId, 2026, 5, "May two");
        }

        var response = await PutAsync(anchorId, Body(activityId, 2026, 5, "Reworded", rating: 4));

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var anchor = await ReloadAsync(anchorId);
        anchor.HighlightNote.Should().Be("Reworded");
        anchor.Rating.Should().Be(4);
    }
}
