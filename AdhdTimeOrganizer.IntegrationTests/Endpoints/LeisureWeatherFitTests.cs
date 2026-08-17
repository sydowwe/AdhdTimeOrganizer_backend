using System.Net;
using System.Net.Http.Json;
using AdhdTimeOrganizer.ActivityProfiles.domain.model;
using AdhdTimeOrganizer.ActivityProfiles.domain.model.entity;
using AdhdTimeOrganizer.ActivityProfiles.domain.service;
using AdhdTimeOrganizer.Core.domain.model.entity.user;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Sydowwe.Framework.infrastructure.persistence.seeder.@interface;
using Sydowwe.Framework.Testing;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Endpoints;

/// <summary>
/// The leisure weather signal over HTTP — <c>GET /leisure-weather-fit</c>.
///
/// <para>The rule itself is covered without a database or a provider in <c>WeatherFitRuleTests</c>. What these
/// pin is everything around it that fails quietly: that the endpoint resolves the day into <b>the caller's own</b>
/// lookup ids and never another user's, that every way the signal can be unavailable is an empty list and a 200
/// rather than an error the picker would have to handle, and that a row's stored <c>Code</c> — not its label —
/// is what decides the match, which is the entire reason the column exists.</para>
///
/// <para>The provider is stubbed throughout. A test that really called Open-Meteo would be testing the weather.</para>
///
/// <para><b>Each test asserts only over the rows it seeded.</b> How many other rows the user has is not this
/// class's business: with <c>Seeding:RunOnStartup</c> on — a flag developers flip locally — booting a host also
/// hands the user <c>ActivityWeatherDependencySeeder</c>'s four defaults, and it does so *after* this class's own
/// <c>SeedDependencyAsync</c> calls, so they cannot be cleared up front. Intersecting with the ids the test owns
/// makes every assertion here say the same thing under either setting.</para>
/// </summary>
[Collection("Postgres")]
public class LeisureWeatherFitTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    private const long UserId = FakeLoggedUserService.TestUserId;
    private const string Route = "/api/leisure-weather-fit";

    /// <summary>Dry, bright and warm: fits none + dry + sunny, and not snow.</summary>
    private static readonly DailyWeather FineDay = new(PrecipitationMm: 0, SnowfallCm: 0, MaxTemperatureC: 24, SunshineHours: 9);

    // ─── the resolved set ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task OnAFineDay_TheMatchingRowsComeBackAsIds()
    {
        await using var db = CreateDbContext();
        await SetLocationAsync(db, "Bratislava, SK");
        var noneId = await SeedDependencyAsync(db, "None", WeatherDependencyCodes.None);
        var dryId = await SeedDependencyAsync(db, "Dry", WeatherDependencyCodes.Dry);
        var sunnyId = await SeedDependencyAsync(db, "Sunny", WeatherDependencyCodes.Sunny);
        var snowId = await SeedDependencyAsync(db, "Snow", WeatherDependencyCodes.Snow);

        var fit = await GetFitAsync(FineDay);

        fit.MatchingWeatherDependencyIds.Intersect([noneId, dryId, sunnyId, snowId])
            .Should().BeEquivalentTo([noneId, dryId, sunnyId],
                "a dry, bright, warm day fits everything except the row that wants snow");
    }

    /// <summary>
    /// The whole point of the <c>Code</c> column. The client's badge would otherwise be a text match, and a user
    /// who renamed their own row — or simply runs the app in Slovak — would silently stop getting one.
    /// </summary>
    [Fact]
    public async Task ARenamedRow_StillMatchesOnItsCode()
    {
        await using var db = CreateDbContext();
        await SetLocationAsync(db, "Bratislava, SK");
        var renamedId = await SeedDependencyAsync(db, "Len keď je pekne", WeatherDependencyCodes.Sunny);

        var fit = await GetFitAsync(FineDay);

        // Its label infers nothing, so the stored code is the only thing that can have matched it.
        fit.MatchingWeatherDependencyIds.Should().Contain(renamedId);
    }

    [Fact]
    public async Task ARowTheUserInvented_MatchesOnWhatItsLabelSays()
    {
        await using var db = CreateDbContext();
        await SetLocationAsync(db, "Bratislava, SK");
        var guessableId = await SeedDependencyAsync(db, "Sunny afternoons", code: null);
        var unknownId = await SeedDependencyAsync(db, "Windy", code: null);

        var fit = await GetFitAsync(FineDay);

        fit.MatchingWeatherDependencyIds.Intersect([guessableId, unknownId])
            .Should().Equal([guessableId],
                "a row with no code gets one guessed from its label; one that guesses nothing takes no part");
    }

    /// <summary>
    /// End to end over the rows a real user actually has: the four <c>ActivityWeatherDependencySeeder</c> writes
    /// on sign-up. This is the only test that pins the seeder's codes reaching the wire — seed a row without one
    /// and it still matches here through label inference, until someone renames it.
    /// </summary>
    [Fact]
    public async Task TheSeededDefaults_ResolveOnAFineDay()
    {
        await using var db = CreateDbContext();
        await SetLocationAsync(db, "Bratislava, SK");

        // Run the seeder directly rather than letting a host boot do it: whether startup seeding runs at all is
        // an appsettings flag a developer flips locally, and a test whose premise is a local toggle is a test
        // that passes or fails for reasons unrelated to the code.
        await SeedDefaultDependenciesAsync();

        var fit = await GetFitAsync(FineDay);

        await using var assertDb = CreateDbContext();
        var seeded = await assertDb.Set<ActivityWeatherDependency>()
            .IgnoreQueryFilters()
            .Where(d => d.UserId == UserId)
            .ToDictionaryAsync(d => d.Text, d => d.Id, CancellationToken);

        seeded.Keys.Should().Contain(["None", "Sunny", "Dry", "Snow"],
            "the per-user defaults are the premise of this test");
        fit.MatchingWeatherDependencyIds.Intersect(seeded.Values)
            .Should().BeEquivalentTo([seeded["None"], seeded["Sunny"], seeded["Dry"]],
                "a fresh user gets a working weather signal without editing anything");
    }

    [Fact]
    public async Task AnotherUsersRows_AreNeverReturned()
    {
        await using var db = CreateDbContext();
        await SetLocationAsync(db, "Bratislava, SK");
        var mineId = await SeedDependencyAsync(db, "Sunny", WeatherDependencyCodes.Sunny);

        var otherUserId = await SeedOtherUserAsync(db, "leisure-weather-b@test.com");
        var theirsId = await SeedDependencyAsync(db, "Sunny", WeatherDependencyCodes.Sunny, otherUserId);

        var fit = await GetFitAsync(FineDay);

        fit.MatchingWeatherDependencyIds.Should().Contain(mineId);
        fit.MatchingWeatherDependencyIds.Should().NotContain(theirsId,
            "the id predicate on the read is the whole guard — an id from another user would be one the client "
            + "then matches its own rows against");
    }

    // ─── every way there is no signal ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task WithNoLocationSet_TheAnswerIsAnEmptySet_NotAnError()
    {
        await using var db = CreateDbContext();
        await SeedDependencyAsync(db, "Sunny", WeatherDependencyCodes.Sunny);

        // Never asked, because there is nothing to ask about.
        var fit = await GetFitAsync(FineDay, expectProviderCalled: false);

        fit.MatchingWeatherDependencyIds.Should().BeEmpty(
            "a user who never filled in a setting must not be shown an error");
    }

    [Fact]
    public async Task WhenTheProviderHasNothing_TheAnswerIsAnEmptySet_NotAnError()
    {
        await using var db = CreateDbContext();
        await SetLocationAsync(db, "Nowhere at all");
        await SeedDependencyAsync(db, "None", WeatherDependencyCodes.None);

        var fit = await GetFitAsync(weather: null);

        fit.MatchingWeatherDependencyIds.Should().BeEmpty(
            "an unresolvable place and a provider outage are the same thing to the picker: no opinion");
    }

    /// <summary>
    /// <c>IDailyWeatherProvider</c> promises never to throw, and the current implementation keeps that promise.
    /// This pins the endpoint's own catch, which is what makes "never an error" true of the route rather than of
    /// whichever provider happens to be registered — a future provider breaking the contract must cost the user
    /// a badge, not the whole request.
    /// </summary>
    [Fact]
    public async Task AProviderThatThrows_StillAnswers200WithNoSignal()
    {
        await using var db = CreateDbContext();
        await SetLocationAsync(db, "Bratislava, SK");
        await SeedDependencyAsync(db, "None", WeatherDependencyCodes.None);

        await using var factory = CreateFactory(TestRoles.AdminAndUser, configureServices: services =>
        {
            services.RemoveAll<IDailyWeatherProvider>();
            services.AddSingleton<IDailyWeatherProvider>(new ThrowingWeatherProvider());
        });

        var response = await factory.CreateClient().GetAsync(Route, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<FitResponse>(JsonOpts, CancellationToken))!
            .MatchingWeatherDependencyIds.Should().BeEmpty();
    }

    [Fact]
    public async Task AnAnonymousCaller_IsRefused()
    {
        (await CreateUnauthenticatedClient().GetAsync(Route, CancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ─── the preference this all hangs off ───────────────────────────────────────────────────────────

    [Fact]
    public async Task TheLocationRoundTripsThroughThePreferencesEndpoint()
    {
        var client = CreateClient();

        (await client.PutAsJsonAsync("/api/user/preferences", new { WeatherLocation = "  Košice, SK  " }, JsonOpts, CancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        var stored = await client.GetFromJsonAsync<UserData>("/api/user/data", JsonOpts, CancellationToken);
        stored!.WeatherLocation.Should().Be("Košice, SK", "stored trimmed, and readable back — a settings input needs both");

        // The base convention is "null leaves it alone", which would otherwise make the field impossible to clear.
        (await client.PutAsJsonAsync("/api/user/preferences", new { FirstDayOfWeek = 0 }, JsonOpts, CancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await client.GetFromJsonAsync<UserData>("/api/user/data", JsonOpts, CancellationToken))!
            .WeatherLocation.Should().Be("Košice, SK", "an unrelated PUT must not wipe it");

        (await client.PutAsJsonAsync("/api/user/preferences", new { WeatherLocation = "" }, JsonOpts, CancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await client.GetFromJsonAsync<UserData>("/api/user/data", JsonOpts, CancellationToken))!
            .WeatherLocation.Should().BeNull("the empty string is how the client clears the setting");
    }

    // ─── helpers ────────────────────────────────────────────────────────────────────────────────────

    private record FitResponse(List<long> MatchingWeatherDependencyIds);

    private record UserData(long Id, string Email, int FirstDayOfWeek, string? WeatherLocation);

    private async Task<FitResponse> GetFitAsync(DailyWeather? weather, bool expectProviderCalled = true)
    {
        var provider = new StubWeatherProvider(weather);

        await using var factory = CreateFactory(TestRoles.AdminAndUser, configureServices: services =>
        {
            services.RemoveAll<IDailyWeatherProvider>();
            services.AddSingleton<IDailyWeatherProvider>(provider);
        });

        var response = await factory.CreateClient().GetAsync(Route, CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK, "the signal is never an error");

        provider.Calls.Should().Be(expectProviderCalled ? 1 : 0,
            "a user with no location set must not cost an outbound call");

        return (await response.Content.ReadFromJsonAsync<FitResponse>(JsonOpts, CancellationToken))!;
    }

    private static async Task SetLocationAsync(DbContext db, string location)
    {
        var user = await db.Set<User>().IgnoreQueryFilters().SingleAsync(u => u.Id == UserId, CancellationToken);
        user.WeatherLocation = location;
        await db.SaveChangesAsync(CancellationToken);
    }

    /// <summary>The per-user defaults, written by the same seeder sign-up runs — idempotent, so it does not matter
    /// whether a host boot has already done it.</summary>
    private async Task SeedDefaultDependenciesAsync()
    {
        using var scope = Fixture.AdminAndUserFactory.Services.CreateScope();
        var seeder = scope.ServiceProvider.GetServices<IPerUserDefaultSeeder>()
            .Single(s => s.SeederName == "ActivityWeatherDependency");

        await seeder.SetupDefaults(UserId, CancellationToken);
    }

    private static async Task<long> SeedDependencyAsync(DbContext db, string text, string? code, long? userId = null)
    {
        var row = new ActivityWeatherDependency { UserId = userId ?? UserId, Text = text, Code = code };
        db.Set<ActivityWeatherDependency>().Add(row);
        await db.SaveChangesAsync(CancellationToken);
        return row.Id;
    }

    private static async Task<long> SeedOtherUserAsync(DbContext db, string email)
    {
        var user = new User
        {
            Email = email,
            UserName = email,
            NormalizedEmail = email.ToUpperInvariant(),
            NormalizedUserName = email.ToUpperInvariant(),
            EmailConfirmed = true,
            SecurityStamp = Guid.NewGuid().ToString("N"),
            ConcurrencyStamp = Guid.NewGuid().ToString("N"),
            Timezone = TimeZoneInfo.Utc
        };
        db.Set<User>().Add(user);
        await db.SaveChangesAsync(CancellationToken);
        return user.Id;
    }

    private sealed class StubWeatherProvider(DailyWeather? weather) : IDailyWeatherProvider
    {
        public int Calls { get; private set; }

        public Task<DailyWeather?> GetTodayAsync(string location, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(weather);
        }
    }

    private sealed class ThrowingWeatherProvider : IDailyWeatherProvider
    {
        public Task<DailyWeather?> GetTodayAsync(string location, CancellationToken ct) =>
            throw new HttpRequestException("the provider fell over");
    }
}
