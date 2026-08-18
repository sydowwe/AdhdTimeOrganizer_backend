using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AdhdTimeOrganizer.Core.domain.model.entity.user;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using AdhdTimeOrganizer.Tracking.domain.model.entity.activityTracking.android;
using AdhdTimeOrganizer.Tracking.domain.model.entity.activityTracking.desktop;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sydowwe.Framework.domain.@enum;
using Sydowwe.Framework.Testing;
using Sydowwe.Framework.Testing.baseTests;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Endpoints;

/// <summary>
/// TEST-7 -- CRUD auth matrix for the two Tracking pattern-mapping entities. Note the entity names:
/// they are <c>TrackerDesktopMappingByPattern</c> / <c>TrackerAndroidMappingByPattern</c>, not the
/// <c>TrackerDesktopMapping</c> / <c>TrackerAndroidMapping</c> the task prompt names -- and since every
/// route here is derived from <c>typeof(TEntity).Name.Kebaberize()</c> by the framework bases, the
/// full name is what actually appears in the URL (<c>tracker-desktop-mapping-by-pattern</c>).
/// <para>
/// Both entities derive from the portal's <c>BaseEntityWithUser</c> shim, so both implement
/// <c>IEntityWithUser</c>, and neither is listed in <c>AppDbContext.UserScopingExcludedTypes</c> (which
/// holds only <c>WebExtensionActivityEntry</c>). They are therefore covered by the global per-user query
/// filter: <c>BaseUpdateEndpoint</c>/<c>BaseDeleteEndpoint</c> load by <c>FindAsync</c>, the foreign row
/// is filtered out before the lookup returns, and the answer is <b>404</b> -- the <c>AuthorizeAsync</c>
/// hook that would produce the bases' default 403 never runs. Hence the <c>UnauthorizedStatus</c>
/// overrides, matching <see cref="ActivityEndpointTests"/> and <c>HistoryCrudAuthMatrixTests</c>.
/// </para>
/// <para>
/// Every endpoint here takes <c>AllowedRoles()</c> from <c>GetDefaultRoles()</c> (User + Admin + Root),
/// so <c>IsAdminOnly</c> is false throughout and the bases' 403-for-User-role guards are skipped.
/// </para>
/// <para>
/// Scope: CRUD auth on the two mapping entities only. Dashboard routing, the
/// <c>WebExtensionActivityEntry</c> combined filter, table partitioning and the retention job are
/// covered by <see cref="TrackingRouteSmokeTests"/>; ingest auth by
/// <see cref="ExtensionActivityTrackingTests"/>. None of that is repeated here.
/// </para>
/// </summary>
file static class Seed
{
    public const string Password = "Test@1234!";

    public const string DesktopUrl = "api/activity-tracking/desktop/settings/tracker-desktop-mapping-by-pattern";
    public const string AndroidUrl = "api/activity-tracking/android/settings/tracker-android-mapping-by-pattern";

    public static async Task<long> SecondUserAsync(IPostgresFixture fixture, string email)
    {
        using var scope = fixture.UnauthenticatedFactory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var user = new User
        {
            Email = email,
            UserName = email,
            NormalizedEmail = email.ToUpperInvariant(),
            NormalizedUserName = email.ToUpperInvariant(),
            EmailConfirmed = true,
            Locale = AvailableLocales.En,
            Timezone = TimeZoneInfo.Utc
        };
        var result = await userManager.CreateAsync(user, Password);
        result.Succeeded.Should().BeTrue();
        return user.Id;
    }

    public static async Task<long> DesktopMappingAsync(DbContext db, long userId, string processName)
    {
        var mapping = new TrackerDesktopMappingByPattern
        {
            // Set explicitly: BaseWithUserEntitySaveChangesAsync only fills in a UserId of 0, so seeding
            // a foreign owner here is not overwritten by the ambient test user.
            UserId = userId,
            ProcessName = $"{processName}-{Guid.NewGuid():N}.exe",
            ProcessNameMatchType = PatternMatchType.Exact,
            IsActive = true,
            IsIgnored = true
        };
        db.Set<TrackerDesktopMappingByPattern>().Add(mapping);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return mapping.Id;
    }

    public static async Task<long> AndroidMappingAsync(DbContext db, long userId, string packageName)
    {
        var mapping = new TrackerAndroidMappingByPattern
        {
            UserId = userId,
            PackageName = $"com.test.{packageName}.{Guid.NewGuid():N}",
            PackageNameMatchType = PatternMatchType.Exact,
            IsActive = true,
            IsIgnored = true
        };
        db.Set<TrackerAndroidMappingByPattern>().Add(mapping);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return mapping.Id;
    }

    /// <summary>
    /// A create payload both mapping validators accept: at least one pattern with its match type, and
    /// exactly one target group (here <c>IsIgnored = true</c>; setting ActivityId or RoleId/CategoryId
    /// as well would trip the "exactly one target" rule).
    /// </summary>
    public static object DesktopCreatePayload() => new
    {
        ProcessName = $"created-{Guid.NewGuid():N}.exe",
        ProcessNameMatchType = PatternMatchType.Exact,
        IsActive = true,
        IsIgnored = true
    };

    public static object AndroidCreatePayload() => new
    {
        PackageName = $"com.created.{Guid.NewGuid():N}",
        PackageNameMatchType = PatternMatchType.Exact,
        IsActive = true,
        IsIgnored = true
    };

    /// <summary>
    /// The update DTOs carry only the pattern fields and <c>IsActive</c> -- no target fields at all, so
    /// the "exactly one target" rule is create-only and an update cannot re-point a mapping.
    /// </summary>
    public static object DesktopUpdatePayload() => new
    {
        ProcessName = $"updated-{Guid.NewGuid():N}.exe",
        ProcessNameMatchType = PatternMatchType.Exact,
        IsActive = false
    };

    public static object AndroidUpdatePayload() => new
    {
        PackageName = $"com.updated.{Guid.NewGuid():N}",
        PackageNameMatchType = PatternMatchType.Exact,
        IsActive = false
    };

    /// <summary>
    /// Grid body with <c>filter: null</c> rather than <c>filter: {}</c>. Both mapping filters declare
    /// <c>required TrackerDesktopMappingTypeEnum Type</c>, and System.Text.Json enforces <c>required</c>
    /// on deserialization -- so an empty object 400s before the handler runs, while an explicit null
    /// satisfies the (nullable) property and leaves <c>ApplyCustomFiltering</c> unreached, which is what
    /// an unfiltered "show me everything" page needs.
    /// </summary>
    public static object UnfilteredGridBody() => new
    {
        useFilter = false,
        filter = (object?)null,
        sortBy = Array.Empty<object>(),
        itemsPerPage = 50,
        page = 1
    };
}

// =====================================================================================================
// TrackerDesktopMappingByPattern CRUD
// =====================================================================================================

[Collection("Postgres")]
public class TrackerDesktopMappingCreateTests(AppDbContextFixture fixture) : BaseCreateEndpointTests(fixture)
{
    protected override string EndpointUrl => Seed.DesktopUrl;
    protected override bool IsAdminOnly => false;
    protected override Task<object> BuildValidPayloadAsync(DbContext db) => Task.FromResult(Seed.DesktopCreatePayload());

    /// <summary>
    /// <c>CreateTrackerDesktopMappingRequest.ToEntity</c> hard-codes <c>UserId = 0</c> and relies entirely
    /// on <c>BaseWithUserEntitySaveChangesAsync</c> to fill in the caller. Nothing in the endpoint asserts
    /// that: if the stamping ever stopped running, the insert would fail on the FK -- or, worse, succeed
    /// against a user id 0 row and produce a mapping no query filter ever hides. This is the one create-side
    /// ownership fact the framework base cannot check, since it only reads the returned id.
    /// </summary>
    [Fact]
    public async Task Create_StampsCallersUserId_NotTheDtosZero()
    {
        var response = await CreateClient().PostAsJsonAsync(EndpointUrl, Seed.DesktopCreatePayload(), JsonOpts);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var id = await response.Content.ReadFromJsonAsync<long>(CancellationToken);

        await using var db = CreateDbContext();
        var created = await db.Set<TrackerDesktopMappingByPattern>().IgnoreQueryFilters().AsNoTracking()
            .FirstAsync(m => m.Id == id, CancellationToken);
        created.UserId.Should().Be(FakeLoggedUserService.TestUserId,
            "the DTO supplies UserId = 0 and only the save-changes stamping turns it into the caller");
    }
}

[Collection("Postgres")]
public class TrackerDesktopMappingUpdateTests(AppDbContextFixture fixture) : BaseUpdateEndpointTests(fixture)
{
    protected override string EndpointUrl => Seed.DesktopUrl;
    protected override bool IsAdminOnly => false;
    protected override HttpStatusCode UnauthorizedStatus => HttpStatusCode.NotFound;

    protected override Task<long> SeedEntityAsync(DbContext db) =>
        Seed.DesktopMappingAsync(db, FakeLoggedUserService.TestUserId, "update");

    protected override Task<object> BuildValidPayloadAsync(DbContext db, long id) => Task.FromResult(Seed.DesktopUpdatePayload());

    protected override async Task<long?> SeedEntityOwnedByOtherUserAsync(DbContext db)
    {
        var otherUserId = await Seed.SecondUserAsync(Fixture, $"desktop-mapping-update-{Guid.NewGuid():N}@test.com");
        return await Seed.DesktopMappingAsync(db, otherUserId, "foreign-update");
    }

    /// <summary>
    /// The base's NotOwner case asserts the status code; this asserts the row. A 404 that still wrote the
    /// update would be the worst outcome of the two, and nothing else in the suite would notice.
    /// </summary>
    [Fact]
    public async Task Update_ForeignId_LeavesTheForeignRowUntouched()
    {
        var otherUserId = await Seed.SecondUserAsync(Fixture, $"desktop-mapping-idor-{Guid.NewGuid():N}@test.com");
        await using var db = CreateDbContext();
        var foreignId = await Seed.DesktopMappingAsync(db, otherUserId, "foreign-idor");

        await using var beforeDb = CreateDbContext();
        var before = await beforeDb.Set<TrackerDesktopMappingByPattern>().IgnoreQueryFilters().AsNoTracking()
            .FirstAsync(m => m.Id == foreignId, CancellationToken);

        var response = await CreateClient().PutAsJsonAsync($"{Seed.DesktopUrl}/{foreignId}", Seed.DesktopUpdatePayload(), JsonOpts);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);

        await using var assertDb = CreateDbContext();
        var after = await assertDb.Set<TrackerDesktopMappingByPattern>().IgnoreQueryFilters().AsNoTracking()
            .FirstAsync(m => m.Id == foreignId, CancellationToken);
        after.ProcessName.Should().Be(before.ProcessName);
        after.IsActive.Should().Be(before.IsActive);
        after.UserId.Should().Be(otherUserId);
    }
}

[Collection("Postgres")]
public class TrackerDesktopMappingDeleteTests(AppDbContextFixture fixture) : BaseDeleteEndpointTests(fixture)
{
    protected override string EndpointUrl => Seed.DesktopUrl;
    protected override bool IsAdminOnly => false;
    protected override HttpStatusCode UnauthorizedStatus => HttpStatusCode.NotFound;

    protected override Task<long> SeedEntityAsync(DbContext db) =>
        Seed.DesktopMappingAsync(db, FakeLoggedUserService.TestUserId, "delete");

    protected override async Task<long?> SeedEntityOwnedByOtherUserAsync(DbContext db)
    {
        var otherUserId = await Seed.SecondUserAsync(Fixture, $"desktop-mapping-delete-{Guid.NewGuid():N}@test.com");
        return await Seed.DesktopMappingAsync(db, otherUserId, "foreign-delete");
    }

    [Fact]
    public async Task Delete_ForeignId_LeavesTheForeignRowInPlace()
    {
        var otherUserId = await Seed.SecondUserAsync(Fixture, $"desktop-mapping-delete-idor-{Guid.NewGuid():N}@test.com");
        await using var db = CreateDbContext();
        var foreignId = await Seed.DesktopMappingAsync(db, otherUserId, "foreign-delete-idor");

        var response = await CreateClient().DeleteAsync($"{Seed.DesktopUrl}/{foreignId}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);

        await using var assertDb = CreateDbContext();
        (await assertDb.Set<TrackerDesktopMappingByPattern>().IgnoreQueryFilters().AnyAsync(m => m.Id == foreignId, CancellationToken))
            .Should().BeTrue("a row the caller was never scoped to see must survive the delete attempt");
    }
}

// =====================================================================================================
// TrackerAndroidMappingByPattern CRUD
// =====================================================================================================

[Collection("Postgres")]
public class TrackerAndroidMappingCreateTests(AppDbContextFixture fixture) : BaseCreateEndpointTests(fixture)
{
    protected override string EndpointUrl => Seed.AndroidUrl;
    protected override bool IsAdminOnly => false;
    protected override Task<object> BuildValidPayloadAsync(DbContext db) => Task.FromResult(Seed.AndroidCreatePayload());

    /// <summary>See <see cref="TrackerDesktopMappingCreateTests.Create_StampsCallersUserId_NotTheDtosZero"/>.</summary>
    [Fact]
    public async Task Create_StampsCallersUserId_NotTheDtosZero()
    {
        var response = await CreateClient().PostAsJsonAsync(EndpointUrl, Seed.AndroidCreatePayload(), JsonOpts);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var id = await response.Content.ReadFromJsonAsync<long>(CancellationToken);

        await using var db = CreateDbContext();
        var created = await db.Set<TrackerAndroidMappingByPattern>().IgnoreQueryFilters().AsNoTracking()
            .FirstAsync(m => m.Id == id, CancellationToken);
        created.UserId.Should().Be(FakeLoggedUserService.TestUserId);
    }
}

[Collection("Postgres")]
public class TrackerAndroidMappingUpdateTests(AppDbContextFixture fixture) : BaseUpdateEndpointTests(fixture)
{
    protected override string EndpointUrl => Seed.AndroidUrl;
    protected override bool IsAdminOnly => false;
    protected override HttpStatusCode UnauthorizedStatus => HttpStatusCode.NotFound;

    protected override Task<long> SeedEntityAsync(DbContext db) =>
        Seed.AndroidMappingAsync(db, FakeLoggedUserService.TestUserId, "update");

    protected override Task<object> BuildValidPayloadAsync(DbContext db, long id) => Task.FromResult(Seed.AndroidUpdatePayload());

    protected override async Task<long?> SeedEntityOwnedByOtherUserAsync(DbContext db)
    {
        var otherUserId = await Seed.SecondUserAsync(Fixture, $"android-mapping-update-{Guid.NewGuid():N}@test.com");
        return await Seed.AndroidMappingAsync(db, otherUserId, "foreign-update");
    }

    /// <summary>See <see cref="TrackerDesktopMappingUpdateTests.Update_ForeignId_LeavesTheForeignRowUntouched"/>.</summary>
    [Fact]
    public async Task Update_ForeignId_LeavesTheForeignRowUntouched()
    {
        var otherUserId = await Seed.SecondUserAsync(Fixture, $"android-mapping-idor-{Guid.NewGuid():N}@test.com");
        await using var db = CreateDbContext();
        var foreignId = await Seed.AndroidMappingAsync(db, otherUserId, "foreign-idor");

        await using var beforeDb = CreateDbContext();
        var before = await beforeDb.Set<TrackerAndroidMappingByPattern>().IgnoreQueryFilters().AsNoTracking()
            .FirstAsync(m => m.Id == foreignId, CancellationToken);

        var response = await CreateClient().PutAsJsonAsync($"{Seed.AndroidUrl}/{foreignId}", Seed.AndroidUpdatePayload(), JsonOpts);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);

        await using var assertDb = CreateDbContext();
        var after = await assertDb.Set<TrackerAndroidMappingByPattern>().IgnoreQueryFilters().AsNoTracking()
            .FirstAsync(m => m.Id == foreignId, CancellationToken);
        after.PackageName.Should().Be(before.PackageName);
        after.IsActive.Should().Be(before.IsActive);
        after.UserId.Should().Be(otherUserId);
    }
}

[Collection("Postgres")]
public class TrackerAndroidMappingDeleteTests(AppDbContextFixture fixture) : BaseDeleteEndpointTests(fixture)
{
    protected override string EndpointUrl => Seed.AndroidUrl;
    protected override bool IsAdminOnly => false;
    protected override HttpStatusCode UnauthorizedStatus => HttpStatusCode.NotFound;

    protected override Task<long> SeedEntityAsync(DbContext db) =>
        Seed.AndroidMappingAsync(db, FakeLoggedUserService.TestUserId, "delete");

    protected override async Task<long?> SeedEntityOwnedByOtherUserAsync(DbContext db)
    {
        var otherUserId = await Seed.SecondUserAsync(Fixture, $"android-mapping-delete-{Guid.NewGuid():N}@test.com");
        return await Seed.AndroidMappingAsync(db, otherUserId, "foreign-delete");
    }

    /// <summary>See <see cref="TrackerDesktopMappingDeleteTests.Delete_ForeignId_LeavesTheForeignRowInPlace"/>.</summary>
    [Fact]
    public async Task Delete_ForeignId_LeavesTheForeignRowInPlace()
    {
        var otherUserId = await Seed.SecondUserAsync(Fixture, $"android-mapping-delete-idor-{Guid.NewGuid():N}@test.com");
        await using var db = CreateDbContext();
        var foreignId = await Seed.AndroidMappingAsync(db, otherUserId, "foreign-delete-idor");

        var response = await CreateClient().DeleteAsync($"{Seed.AndroidUrl}/{foreignId}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);

        await using var assertDb = CreateDbContext();
        (await assertDb.Set<TrackerAndroidMappingByPattern>().IgnoreQueryFilters().AnyAsync(m => m.Id == foreignId, CancellationToken))
            .Should().BeTrue("a row the caller was never scoped to see must survive the delete attempt");
    }
}

// =====================================================================================================
// The two settings grids. GridTrackerDesktopMappingEndpoint / GridTrackerAndroidMappingEndpoint both
// derive from BaseGridEndpoint, but BaseGridEndpointTests does NOT fit them: its GridBody() is a static
// helper the tests call with no seam to change, and it sends `filter: {}` -- which both mapping filters
// reject, because each declares `required TrackerDesktopMappingTypeEnum Type` and System.Text.Json
// enforces `required` during deserialization. The base's happy path would 400 on a correctly behaving
// endpoint. Hand-written here with `filter: null` instead.
//
// Neither grid overrides ApplyUserScoping, so their scoping is the global IEntityWithUser query filter
// and nothing else -- which is exactly why the cross-user case below is worth pinning: drop these
// entities out of the filter (or add them to UserScopingExcludedTypes the way WebExtensionActivityEntry
// is) and every user's mapping rules become readable, with no error anywhere.
// =====================================================================================================

[Collection("Postgres")]
public class TrackerDesktopMappingGridTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    private record GridResponse(JsonElement[] Items, int ItemsCount, int PageCount);

    [Fact]
    public async Task Grid_ReturnsCallersOwnMapping()
    {
        await using var db = CreateDbContext();
        var id = await Seed.DesktopMappingAsync(db, FakeLoggedUserService.TestUserId, "grid");

        var response = await CreateClient().PostAsJsonAsync($"{Seed.DesktopUrl}/filtered-table", Seed.UnfilteredGridBody(), JsonOpts);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<GridResponse>(JsonOpts, CancellationToken);
        body!.Items.Select(i => i.GetProperty("id").GetInt64()).Should().Contain(id);
    }

    [Fact]
    public async Task Grid_ExcludesOtherUsersMappings()
    {
        await using var db = CreateDbContext();
        var ownId = await Seed.DesktopMappingAsync(db, FakeLoggedUserService.TestUserId, "grid-own");

        var otherUserId = await Seed.SecondUserAsync(Fixture, $"desktop-mapping-grid-{Guid.NewGuid():N}@test.com");
        var foreignId = await Seed.DesktopMappingAsync(db, otherUserId, "grid-foreign");

        var response = await CreateClient().PostAsJsonAsync($"{Seed.DesktopUrl}/filtered-table", Seed.UnfilteredGridBody(), JsonOpts);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<GridResponse>(JsonOpts, CancellationToken);
        var ids = body!.Items.Select(i => i.GetProperty("id").GetInt64()).ToList();

        ids.Should().Contain(ownId, "the caller's own mapping must be returned");
        ids.Should().NotContain(foreignId, "another user's mapping rules must never leak into the settings grid");
    }

    [Fact]
    public async Task Grid_Unauthenticated_Returns401()
    {
        var response = await CreateUnauthenticatedClient()
            .PostAsJsonAsync($"{Seed.DesktopUrl}/filtered-table", Seed.UnfilteredGridBody(), JsonOpts);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}

[Collection("Postgres")]
public class TrackerAndroidMappingGridTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    private record GridResponse(JsonElement[] Items, int ItemsCount, int PageCount);

    [Fact]
    public async Task Grid_ReturnsCallersOwnMapping()
    {
        await using var db = CreateDbContext();
        var id = await Seed.AndroidMappingAsync(db, FakeLoggedUserService.TestUserId, "grid");

        var response = await CreateClient().PostAsJsonAsync($"{Seed.AndroidUrl}/filtered-table", Seed.UnfilteredGridBody(), JsonOpts);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<GridResponse>(JsonOpts, CancellationToken);
        body!.Items.Select(i => i.GetProperty("id").GetInt64()).Should().Contain(id);
    }

    [Fact]
    public async Task Grid_ExcludesOtherUsersMappings()
    {
        await using var db = CreateDbContext();
        var ownId = await Seed.AndroidMappingAsync(db, FakeLoggedUserService.TestUserId, "grid-own");

        var otherUserId = await Seed.SecondUserAsync(Fixture, $"android-mapping-grid-{Guid.NewGuid():N}@test.com");
        var foreignId = await Seed.AndroidMappingAsync(db, otherUserId, "grid-foreign");

        var response = await CreateClient().PostAsJsonAsync($"{Seed.AndroidUrl}/filtered-table", Seed.UnfilteredGridBody(), JsonOpts);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<GridResponse>(JsonOpts, CancellationToken);
        var ids = body!.Items.Select(i => i.GetProperty("id").GetInt64()).ToList();

        ids.Should().Contain(ownId, "the caller's own mapping must be returned");
        ids.Should().NotContain(foreignId, "another user's mapping rules must never leak into the settings grid");
    }

    [Fact]
    public async Task Grid_Unauthenticated_Returns401()
    {
        var response = await CreateUnauthenticatedClient()
            .PostAsJsonAsync($"{Seed.AndroidUrl}/filtered-table", Seed.UnfilteredGridBody(), JsonOpts);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
