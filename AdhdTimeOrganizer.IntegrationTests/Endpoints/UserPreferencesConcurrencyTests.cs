using System.Net;
using System.Net.Http.Json;
using AdhdTimeOrganizer.Core.domain.model.entity.user;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using AdhdTimeOrganizer.infrastructure.persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Sydowwe.Framework.Testing;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Endpoints;

/// <summary>
/// <c>PUT /user/preferences</c> when another write to the same user row lands between the endpoint's load and its
/// save.
/// <para>
/// The save is Identity's <c>UserManager.UpdateAsync</c>, which writes the whole row and checks it was not moved
/// underneath (<c>ConcurrencyStamp</c>, and <c>row_version</c> = <c>xmin</c>, which any UPDATE moves — the morning
/// briefing job's <c>ExecuteUpdate</c> mark included). So the loser of two overlapping writes used to get a 400
/// "Optimistic concurrency failure" for a request that was perfectly valid: the frontend's boot-time time-zone sync
/// and a settings toggle collide exactly like this. Each request names only the columns it changes, so the right
/// answer is to re-read the row and apply them again, keeping both writes. The check itself must stay: without it
/// the whole-row update would put the other writer's columns back to what this request loaded.
/// </para>
/// <para>
/// A race does not reproduce on demand, so the competing write is forced: an interceptor on the test host's
/// context makes a second context write the same row the first time the endpoint saves the user.
/// </para>
/// </summary>
[Collection("Postgres")]
public class UserPreferencesConcurrencyTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    private const long UserId = FakeLoggedUserService.TestUserId;
    private const string CompetingZone = "Asia/Tokyo";

    [Fact(DisplayName = "A preference save that loses a race to another write is retried, and both writes survive")]
    public async Task ASaveThatLosesARace_IsRetried_AndKeepsTheOtherWrite()
    {
        var competitor = new CompetingWriteInterceptor(Fixture.CreateDbContext, UserId);
        await using var factory = CreateFactory(TestRoles.AdminAndUser, configureServices: services =>
            services.ConfigureDbContext<AppDbContext>(options => options.AddInterceptors(competitor)));

        var response = await factory.CreateClient()
            .PutAsJsonAsync("/api/user/preferences", new { MorningBriefingOffsetMinutes = 45 }, JsonOpts, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync(CancellationToken));
        competitor.Fired.Should().BeTrue("the competing write must actually have happened, or this proves nothing");
        competitor.UserSaves.Should().Be(2, "the first save lost to the competing write and the retry won");

        await using var db = (AppDbContext)Fixture.CreateDbContext();
        var user = await db.Users.IgnoreQueryFilters().AsNoTracking().SingleAsync(u => u.Id == UserId, CancellationToken);
        user.MorningBriefingOffsetMinutes.Should().Be(45, "the request's own change");
        user.Timezone.Id.Should().Be(CompetingZone, "the competing write, which the retry must not overwrite with what it first loaded");
    }

    [Fact(DisplayName = "Two overlapping preference saves to different fields both answer 204 and both stick")]
    public async Task TwoOverlappingSaves_ToDifferentFields_BothStick()
    {
        // Whether the two actually overlap is up to the scheduler, so this is the weak half beside the forced race
        // above: it holds either way, and before the retry it failed whenever they did overlap.
        var responses = await Task.WhenAll(
            CreateClient().PutAsJsonAsync("/api/user/preferences", new { MorningBriefingOffsetMinutes = 90 }, JsonOpts, CancellationToken),
            CreateClient().PutAsJsonAsync("/api/user/preferences", new { FirstDayOfWeek = 0 }, JsonOpts, CancellationToken));

        responses.Should().OnlyContain(r => r.StatusCode == HttpStatusCode.NoContent);

        await using var db = (AppDbContext)Fixture.CreateDbContext();
        var user = await db.Users.IgnoreQueryFilters().AsNoTracking().SingleAsync(u => u.Id == UserId, CancellationToken);
        user.MorningBriefingOffsetMinutes.Should().Be(90);
        user.FirstDayOfWeek.Should().Be(0);
    }

    /// <summary>
    /// The first time the endpoint's context is about to save a modified <see cref="User"/>, a second context writes
    /// the same row first — what the frontend's time-zone sync does when it lands mid-save. Fires once, so the retry
    /// that follows meets no competitor.
    /// </summary>
    private sealed class CompetingWriteInterceptor(Func<DbContext> createDbContext, long userId) : SaveChangesInterceptor
    {
        public bool Fired { get; private set; }
        public int UserSaves { get; private set; }

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context?.ChangeTracker.Entries<User>().Any(e => e.State == EntityState.Modified) != true)
                return result;

            UserSaves++;
            if (Fired)
                return result;

            Fired = true;
            await using var other = createDbContext();
            var user = await other.Set<User>().IgnoreQueryFilters().SingleAsync(u => u.Id == userId, cancellationToken);
            user.Timezone = TimeZoneInfo.FindSystemTimeZoneById(CompetingZone);
            await other.SaveChangesAsync(cancellationToken);
            return result;
        }
    }
}
