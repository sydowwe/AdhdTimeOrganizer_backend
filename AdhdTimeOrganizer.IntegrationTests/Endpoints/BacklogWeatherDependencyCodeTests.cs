using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AdhdTimeOrganizer.ActivityProfiles.domain.model;
using AdhdTimeOrganizer.ActivityProfiles.domain.model.entity;
using AdhdTimeOrganizer.ActivityProfiles.domain.model.@enum;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sydowwe.Framework.infrastructure.persistence.seeder.@interface;
using Sydowwe.Framework.Testing;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Endpoints;

/// <summary>
/// <c>weatherDependencyCode</c> on a backlog row — the effective <see cref="WeatherDependencyCodes"/> value of
/// its weather lookup, which is how the leisure picker tells a "don't care" row from one that really depends on
/// the weather. The weather-fit set always contains <c>none</c>, so without this field every indoor entry got a
/// "good weather today" badge.
///
/// <para>The field is filled in two stages — the stored code in the SQL projection, the label inference after
/// the load — and each of the three read routes runs the second stage on its own. Forgetting it on one route is
/// silent: that route answers <c>null</c> for every user-created row, which the client reads as "no opinion".
/// So every assertion here reads the row through <b>all three</b> routes and requires them to agree.</para>
///
/// <para>The key is asserted present even when null: the client reads a missing key and a null the same way
/// today, but the contract says null, and a serializer change that started dropping it should show up here.</para>
/// </summary>
[Collection("Postgres")]
public class BacklogWeatherDependencyCodeTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    private const long UserId = FakeLoggedUserService.TestUserId;
    private const string Route = "api/activity-backlog-profile";

    [Fact]
    public async Task TheSeededDefaults_CarryTheirStoredCodes()
    {
        await SeedDefaultDependenciesAsync();
        await using var db = CreateDbContext();
        var seeded = await SeededDependencyIdsAsync(db);

        var noneProfile = await SeedBacklogAsync(db, "Read a book", seeded["None"]);
        var sunnyProfile = await SeedBacklogAsync(db, "Picnic", seeded["Sunny"]);
        var dryProfile = await SeedBacklogAsync(db, "Bike ride", seeded["Dry"]);
        var snowProfile = await SeedBacklogAsync(db, "Sledging", seeded["Snow"]);

        (await CodeOnEveryRouteAsync(noneProfile)).Should().Be(WeatherDependencyCodes.None,
            "the 'any weather' row must be recognisable as such — it is the one the fit set always contains");
        (await CodeOnEveryRouteAsync(sunnyProfile)).Should().Be(WeatherDependencyCodes.Sunny);
        (await CodeOnEveryRouteAsync(dryProfile)).Should().Be(WeatherDependencyCodes.Dry);
        (await CodeOnEveryRouteAsync(snowProfile)).Should().Be(WeatherDependencyCodes.Snow);
    }

    [Fact]
    public async Task AUserCreatedRow_GetsTheCodeItsLabelInfers()
    {
        await using var db = CreateDbContext();
        var slnecnoId = await SeedDependencyAsync(db, "Slnečno", code: null);
        var profileId = await SeedBacklogAsync(db, "Opaľovanie", slnecnoId);

        (await CodeOnEveryRouteAsync(profileId)).Should().Be(WeatherDependencyCodes.Sunny,
            "user-created rows never store a code — sending only the column would leave the client guessing from labels");
    }

    [Fact]
    public async Task AnUnrecognisableLabel_IsNull()
    {
        await using var db = CreateDbContext();
        var unknownId = await SeedDependencyAsync(db, "Mondscheinwetter", code: null);
        var profileId = await SeedBacklogAsync(db, "Nachtspaziergang", unknownId);

        (await CodeOnEveryRouteAsync(profileId)).Should().BeNull(
            "a label no locale recognises is 'no weather opinion', not a guess");
    }

    [Fact]
    public async Task ARenamedSeededRow_KeepsItsStoredCode_OverWhatTheNewLabelSays()
    {
        await SeedDefaultDependenciesAsync();
        await using var db = CreateDbContext();
        var noneId = (await SeededDependencyIdsAsync(db))["None"];

        var row = await db.Set<ActivityWeatherDependency>().IgnoreQueryFilters().SingleAsync(d => d.Id == noneId, CancellationToken);
        row.Text = "Sunny days";
        await db.SaveChangesAsync(CancellationToken);

        var profileId = await SeedBacklogAsync(db, "Board games", noneId);

        (await CodeOnEveryRouteAsync(profileId)).Should().Be(WeatherDependencyCodes.None,
            "the label now infers 'sunny', and only the stored code winning keeps a rename from changing what the row means");
    }

    // ─── helpers ────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The row's <c>weatherDependencyCode</c> read through the grid, GetById and GetAll, asserted identical on all
    /// three. Each route runs the post-load inference separately, so agreement is the thing that can break.
    /// </summary>
    private async Task<string?> CodeOnEveryRouteAsync(long profileId)
    {
        var client = CreateClient();

        var gridBody = new { useFilter = false, filter = new { }, sortBy = Array.Empty<object>(), itemsPerPage = 100, page = 1 };
        var gridResponse = await client.PostAsJsonAsync($"{Route}/grid", gridBody, JsonOpts, CancellationToken);
        gridResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var grid = (await gridResponse.Content.ReadFromJsonAsync<GridResponse>(JsonOpts, CancellationToken))!;
        var fromGrid = CodeOf(grid.Items.Single(i => i.GetProperty("id").GetInt64() == profileId));
        var byIdResponse = await client.GetAsync($"{Route}/{profileId}", CancellationToken);
        byIdResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var fromGetById = CodeOf((await byIdResponse.Content.ReadFromJsonAsync<JsonElement>(JsonOpts, CancellationToken))!);

        var allResponse = await client.GetAsync(Route, CancellationToken);
        allResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var all = (await allResponse.Content.ReadFromJsonAsync<JsonElement[]>(JsonOpts, CancellationToken))!;
        var fromGetAll = CodeOf(all.Single(i => i.GetProperty("id").GetInt64() == profileId));

        fromGetById.Should().Be(fromGrid, "GetById must finish the code the same way the grid does");
        fromGetAll.Should().Be(fromGrid, "GetAll must finish the code the same way the grid does");

        return fromGrid;
    }

    private static string? CodeOf(JsonElement item)
    {
        item.TryGetProperty("weatherDependencyCode", out var code).Should().BeTrue(
            "the field is on the wire at the top level of the row, null included");
        return code.ValueKind == JsonValueKind.Null ? null : code.GetString();
    }

    /// <summary>The per-user defaults, written by the same seeder sign-up runs — idempotent.</summary>
    private async Task SeedDefaultDependenciesAsync()
    {
        using var scope = Fixture.AdminAndUserFactory.Services.CreateScope();
        var seeder = scope.ServiceProvider.GetServices<IPerUserDefaultSeeder>()
            .Single(s => s.SeederName == "ActivityWeatherDependency");

        await seeder.SetupDefaults(UserId, CancellationToken);
    }

    private static Task<Dictionary<string, long>> SeededDependencyIdsAsync(DbContext db) =>
        db.Set<ActivityWeatherDependency>()
            .IgnoreQueryFilters()
            .Where(d => d.UserId == UserId && d.Code != null)
            .ToDictionaryAsync(d => d.Text, d => d.Id, CancellationToken);

    private static async Task<long> SeedDependencyAsync(DbContext db, string text, string? code)
    {
        var row = new ActivityWeatherDependency { UserId = UserId, Text = text, Code = code };
        db.Set<ActivityWeatherDependency>().Add(row);
        await db.SaveChangesAsync(CancellationToken);
        return row.Id;
    }

    /// <summary>A backlog profile pointing at the given weather row; the other two lookups are throwaways.</summary>
    private static async Task<long> SeedBacklogAsync(DbContext db, string name, long weatherDependencyId)
    {
        var activityId = await ActivityProfileTestSeedHelper.SeedActivityAsync(db, UserId, name);
        var locationTypeId = await ActivityProfileTestSeedHelper.SeedLookupAsync<ActivityLocationType>(db, UserId, $"{name} Home");
        var costTierId = await ActivityProfileTestSeedHelper.SeedLookupAsync<ActivityExpectedCostTier>(db, UserId, $"{name} Free");

        var profile = new ActivityBacklogProfile
        {
            ActivityId = activityId,
            LocationTypeId = locationTypeId,
            WeatherDependencyId = weatherDependencyId,
            ExpectedCostTierId = costTierId,
            EnergyLevel = EnergyLevel.Low,
            MinParticipants = 1,
            DurationMinutes = 30
        };
        db.Set<ActivityBacklogProfile>().Add(profile);
        await db.SaveChangesAsync(CancellationToken);
        return profile.Id;
    }

    private record GridResponse(JsonElement[] Items, int ItemsCount, int PageCount);
}
