using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using AdhdTimeOrganizer.ActivityProfiles.domain.model.entity;
using AdhdTimeOrganizer.ActivityProfiles.domain.model.@enum;
using AdhdTimeOrganizer.Core.domain.model.@enum;
using AdhdTimeOrganizer.Core.domain.model.entity.activity;
using AdhdTimeOrganizer.Core.domain.model.entity.@base;
using AdhdTimeOrganizer.Core.domain.model.entity.user;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using AdhdTimeOrganizer.Planning.domain.model.entity;
using AdhdTimeOrganizer.Planning.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.Planning.domain.model.@enum;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sydowwe.Framework.domain.entity.user;
using Sydowwe.Framework.domain.@enum;
using Sydowwe.Framework.Testing;
using Sydowwe.Framework.Testing.baseTests;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Endpoints;

/// <summary>
/// TEST-5 / Sections B-G — Activity, ActivityRole, ActivityCategory, MemoryAnchor and the four
/// Activity*Lookup entities. Unlike the three Activity*Profile grids (see
/// <see cref="ActivityProfileGridTests"/>), every one of these IS IEntityWithUser and is protected by
/// AppDbContext's global query filter, so a cross-user id is simply not found by
/// Set&lt;TEntity&gt;().FindAsync/.Where -- these tests exist to prove that filter is actually wired on
/// every route, not to hand-scope anything. This is also the portal's first use of the 13 abstract
/// framework test bases (Sydowwe.Framework.Testing.baseTests); every route here answers 404 (not the
/// bases' default 403) for a cross-user id, because the row is filtered out before any AuthorizeAsync
/// hook runs -- see the UnauthorizedStatus overrides below.
/// </summary>
file static class Seed
{
    public const string Password = "Test@1234!";

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

    public static async Task<long> RoleAsync(DbContext db, long userId, string name)
    {
        var role = new ActivityRole { UserId = userId, Name = name, Color = "#112233" };
        db.Set<ActivityRole>().Add(role);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return role.Id;
    }

    public static async Task<long> CategoryAsync(DbContext db, long userId, string name)
    {
        var category = new ActivityCategory { UserId = userId, Name = name, Color = "#334455" };
        db.Set<ActivityCategory>().Add(category);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return category.Id;
    }

    public static async Task<long> ActivityAsync(DbContext db, long userId, string name)
    {
        var roleId = await RoleAsync(db, userId, $"{name} Role {Guid.NewGuid():N}");
        var activity = new Activity { UserId = userId, Name = name, RoleId = roleId };
        db.Set<Activity>().Add(activity);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return activity.Id;
    }

    public static async Task<long> LookupAsync<TLookup>(DbContext db, long userId, string text)
        where TLookup : BaseLookupWithUser, new()
    {
        var lookup = new TLookup { UserId = userId, Text = text };
        db.Set<TLookup>().Add(lookup);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return lookup.Id;
    }

    public static async Task<long> MemoryAnchorAsync(DbContext db, long userId, string note = "Anchor note")
    {
        var activityId = await ActivityAsync(db, userId, $"Anchor Activity {Guid.NewGuid():N}");
        var anchor = new MemoryAnchor
        {
            UserId = userId,
            ActivityId = activityId,
            AnchorMonth = 6,
            AnchorYear = 2024,
            HighlightNote = note,
            Rating = 5
        };
        db.Set<MemoryAnchor>().Add(anchor);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return anchor.Id;
    }
}

// =====================================================================================================
// Activity
// =====================================================================================================

[Collection("Postgres")]
public class ActivityGetByIdTests(AppDbContextFixture fixture) : BaseGetByIdEndpointTests(fixture)
{
    protected override string EndpointUrl => "api/activity";
    protected override bool IsAdminOnly => false;
    protected override HttpStatusCode UnauthorizedStatus => HttpStatusCode.NotFound;
    protected override Task<long> SeedEntityAsync(DbContext db) => Seed.ActivityAsync(db, FakeLoggedUserService.TestUserId, "GetById Activity");

    protected override async Task<long?> SeedEntityOwnedByOtherUserAsync(DbContext db)
    {
        var otherUserId = await Seed.SecondUserAsync(Fixture, $"activity-getbyid-{Guid.NewGuid():N}@test.com");
        return await Seed.ActivityAsync(db, otherUserId, "Foreign Activity");
    }
}

[Collection("Postgres")]
public class ActivityGridTests(AppDbContextFixture fixture) : BaseGridEndpointTests(fixture)
{
    protected override string EndpointUrl => "api/activity/filtered-table";
    protected override bool IsAdminOnly => false;
    protected override Task<long> SeedEntityAsync(DbContext db) => Seed.ActivityAsync(db, FakeLoggedUserService.TestUserId, "Grid Activity");
}

[Collection("Postgres")]
public class ActivityCreateTests(AppDbContextFixture fixture) : BaseCreateEndpointTests(fixture)
{
    protected override string EndpointUrl => "api/activity";
    protected override bool IsAdminOnly => false;

    protected override async Task<object> BuildValidPayloadAsync(DbContext db)
    {
        var roleId = await Seed.RoleAsync(db, FakeLoggedUserService.TestUserId, $"Create Role {Guid.NewGuid():N}");
        return new { Name = $"Created Activity {Guid.NewGuid():N}", IsUnavoidable = false, RoleId = roleId };
    }
}

[Collection("Postgres")]
public class ActivityUpdateTests(AppDbContextFixture fixture) : BaseUpdateEndpointTests(fixture)
{
    protected override string EndpointUrl => "api/activity";
    protected override bool IsAdminOnly => false;
    protected override HttpStatusCode UnauthorizedStatus => HttpStatusCode.NotFound;
    protected override Task<long> SeedEntityAsync(DbContext db) => Seed.ActivityAsync(db, FakeLoggedUserService.TestUserId, "Update Activity");

    protected override async Task<object> BuildValidPayloadAsync(DbContext db, long id)
    {
        var roleId = await Seed.RoleAsync(db, FakeLoggedUserService.TestUserId, $"Update Role {Guid.NewGuid():N}");
        return new { Name = "Updated Activity", IsUnavoidable = true, RoleId = roleId };
    }

    protected override async Task<long?> SeedEntityOwnedByOtherUserAsync(DbContext db)
    {
        var otherUserId = await Seed.SecondUserAsync(Fixture, $"activity-update-{Guid.NewGuid():N}@test.com");
        return await Seed.ActivityAsync(db, otherUserId, "Foreign Update Activity");
    }
}

[Collection("Postgres")]
public class ActivityDeleteTests(AppDbContextFixture fixture) : BaseDeleteEndpointTests(fixture)
{
    protected override string EndpointUrl => "api/activity";
    protected override bool IsAdminOnly => false;
    protected override HttpStatusCode UnauthorizedStatus => HttpStatusCode.NotFound;
    protected override Task<long> SeedEntityAsync(DbContext db) => Seed.ActivityAsync(db, FakeLoggedUserService.TestUserId, "Delete Activity");

    protected override async Task<long?> SeedEntityOwnedByOtherUserAsync(DbContext db)
    {
        var otherUserId = await Seed.SecondUserAsync(Fixture, $"activity-delete-{Guid.NewGuid():N}@test.com");
        return await Seed.ActivityAsync(db, otherUserId, "Foreign Delete Activity");
    }
}

[Collection("Postgres")]
public class ActivitySelectOptionsTests(AppDbContextFixture fixture) : BaseGetSelectOptionsEndpointTests(fixture)
{
    protected override string EndpointUrl => "api/activity/all-options";
    protected override bool IsAdminOnly => false;
    protected override Task<long> SeedEntityAsync(DbContext db) => Seed.ActivityAsync(db, FakeLoggedUserService.TestUserId, "SelectOptions Activity");
}

// =====================================================================================================
// ActivityRole (no BatchDelete endpoint exists for this entity)
// =====================================================================================================

[Collection("Postgres")]
public class ActivityRoleGetByIdTests(AppDbContextFixture fixture) : BaseGetByIdEndpointTests(fixture)
{
    protected override string EndpointUrl => "api/activity-role";
    protected override bool IsAdminOnly => false;
    protected override HttpStatusCode UnauthorizedStatus => HttpStatusCode.NotFound;
    protected override Task<long> SeedEntityAsync(DbContext db) => Seed.RoleAsync(db, FakeLoggedUserService.TestUserId, "GetById Role");

    protected override async Task<long?> SeedEntityOwnedByOtherUserAsync(DbContext db)
    {
        var otherUserId = await Seed.SecondUserAsync(Fixture, $"role-getbyid-{Guid.NewGuid():N}@test.com");
        return await Seed.RoleAsync(db, otherUserId, "Foreign Role");
    }
}

[Collection("Postgres")]
public class ActivityRoleGridTests(AppDbContextFixture fixture) : BaseGridEndpointTests(fixture)
{
    protected override string EndpointUrl => "api/activity-role/filtered-table";
    protected override bool IsAdminOnly => false;
    protected override Task<long> SeedEntityAsync(DbContext db) => Seed.RoleAsync(db, FakeLoggedUserService.TestUserId, "Grid Role");
}

[Collection("Postgres")]
public class ActivityRoleCreateTests(AppDbContextFixture fixture) : BaseCreateEndpointTests(fixture)
{
    protected override string EndpointUrl => "api/activity-role";
    protected override bool IsAdminOnly => false;
    protected override Task<object> BuildValidPayloadAsync(DbContext db) =>
        Task.FromResult<object>(new { Name = $"Created Role {Guid.NewGuid():N}", Color = "#abcdef" });
}

[Collection("Postgres")]
public class ActivityRoleUpdateTests(AppDbContextFixture fixture) : BaseUpdateEndpointTests(fixture)
{
    protected override string EndpointUrl => "api/activity-role";
    protected override bool IsAdminOnly => false;
    protected override HttpStatusCode UnauthorizedStatus => HttpStatusCode.NotFound;
    protected override Task<long> SeedEntityAsync(DbContext db) => Seed.RoleAsync(db, FakeLoggedUserService.TestUserId, "Update Role");
    protected override Task<object> BuildValidPayloadAsync(DbContext db, long id) =>
        Task.FromResult<object>(new { Name = "Updated Role", Color = "#123456" });

    protected override async Task<long?> SeedEntityOwnedByOtherUserAsync(DbContext db)
    {
        var otherUserId = await Seed.SecondUserAsync(Fixture, $"role-update-{Guid.NewGuid():N}@test.com");
        return await Seed.RoleAsync(db, otherUserId, "Foreign Update Role");
    }
}

[Collection("Postgres")]
public class ActivityRoleDeleteTests(AppDbContextFixture fixture) : BaseDeleteEndpointTests(fixture)
{
    protected override string EndpointUrl => "api/activity-role";
    protected override bool IsAdminOnly => false;
    protected override HttpStatusCode UnauthorizedStatus => HttpStatusCode.NotFound;
    protected override Task<long> SeedEntityAsync(DbContext db) => Seed.RoleAsync(db, FakeLoggedUserService.TestUserId, "Delete Role");

    protected override async Task<long?> SeedEntityOwnedByOtherUserAsync(DbContext db)
    {
        var otherUserId = await Seed.SecondUserAsync(Fixture, $"role-delete-{Guid.NewGuid():N}@test.com");
        return await Seed.RoleAsync(db, otherUserId, "Foreign Delete Role");
    }
}

[Collection("Postgres")]
public class ActivityRoleSelectOptionsTests(AppDbContextFixture fixture) : BaseGetSelectOptionsEndpointTests(fixture)
{
    protected override string EndpointUrl => "api/activity-role/all-options";
    protected override bool IsAdminOnly => false;
    protected override Task<long> SeedEntityAsync(DbContext db) => Seed.RoleAsync(db, FakeLoggedUserService.TestUserId, "SelectOptions Role");
}

// =====================================================================================================
// ActivityCategory (has a BatchDelete endpoint)
// =====================================================================================================

[Collection("Postgres")]
public class ActivityCategoryGetByIdTests(AppDbContextFixture fixture) : BaseGetByIdEndpointTests(fixture)
{
    protected override string EndpointUrl => "api/activity-category";
    protected override bool IsAdminOnly => false;
    protected override HttpStatusCode UnauthorizedStatus => HttpStatusCode.NotFound;
    protected override Task<long> SeedEntityAsync(DbContext db) => Seed.CategoryAsync(db, FakeLoggedUserService.TestUserId, "GetById Category");

    protected override async Task<long?> SeedEntityOwnedByOtherUserAsync(DbContext db)
    {
        var otherUserId = await Seed.SecondUserAsync(Fixture, $"category-getbyid-{Guid.NewGuid():N}@test.com");
        return await Seed.CategoryAsync(db, otherUserId, "Foreign Category");
    }
}

[Collection("Postgres")]
public class ActivityCategoryGridTests(AppDbContextFixture fixture) : BaseGridEndpointTests(fixture)
{
    protected override string EndpointUrl => "api/activity-category/filtered-table";
    protected override bool IsAdminOnly => false;
    protected override Task<long> SeedEntityAsync(DbContext db) => Seed.CategoryAsync(db, FakeLoggedUserService.TestUserId, "Grid Category");
}

[Collection("Postgres")]
public class ActivityCategoryCreateTests(AppDbContextFixture fixture) : BaseCreateEndpointTests(fixture)
{
    protected override string EndpointUrl => "api/activity-category";
    protected override bool IsAdminOnly => false;
    protected override Task<object> BuildValidPayloadAsync(DbContext db) =>
        Task.FromResult<object>(new { Name = $"Created Category {Guid.NewGuid():N}", Color = "#abcdef" });
}

[Collection("Postgres")]
public class ActivityCategoryUpdateTests(AppDbContextFixture fixture) : BaseUpdateEndpointTests(fixture)
{
    protected override string EndpointUrl => "api/activity-category";
    protected override bool IsAdminOnly => false;
    protected override HttpStatusCode UnauthorizedStatus => HttpStatusCode.NotFound;
    protected override Task<long> SeedEntityAsync(DbContext db) => Seed.CategoryAsync(db, FakeLoggedUserService.TestUserId, "Update Category");
    protected override Task<object> BuildValidPayloadAsync(DbContext db, long id) =>
        Task.FromResult<object>(new { Name = "Updated Category", Color = "#123456" });

    protected override async Task<long?> SeedEntityOwnedByOtherUserAsync(DbContext db)
    {
        var otherUserId = await Seed.SecondUserAsync(Fixture, $"category-update-{Guid.NewGuid():N}@test.com");
        return await Seed.CategoryAsync(db, otherUserId, "Foreign Update Category");
    }
}

[Collection("Postgres")]
public class ActivityCategoryDeleteTests(AppDbContextFixture fixture) : BaseDeleteEndpointTests(fixture)
{
    protected override string EndpointUrl => "api/activity-category";
    protected override bool IsAdminOnly => false;
    protected override HttpStatusCode UnauthorizedStatus => HttpStatusCode.NotFound;
    protected override Task<long> SeedEntityAsync(DbContext db) => Seed.CategoryAsync(db, FakeLoggedUserService.TestUserId, "Delete Category");

    protected override async Task<long?> SeedEntityOwnedByOtherUserAsync(DbContext db)
    {
        var otherUserId = await Seed.SecondUserAsync(Fixture, $"category-delete-{Guid.NewGuid():N}@test.com");
        return await Seed.CategoryAsync(db, otherUserId, "Foreign Delete Category");
    }
}

[Collection("Postgres")]
public class ActivityCategorySelectOptionsTests(AppDbContextFixture fixture) : BaseGetSelectOptionsEndpointTests(fixture)
{
    protected override string EndpointUrl => "api/activity-category/all-options";
    protected override bool IsAdminOnly => false;
    protected override Task<long> SeedEntityAsync(DbContext db) => Seed.CategoryAsync(db, FakeLoggedUserService.TestUserId, "SelectOptions Category");
}

/// <summary>
/// BaseBatchDeleteEndpointTests.NotOwner_Returns403 hardcodes an expectation of 403 with no override
/// point (unlike every other base, which exposes UnauthorizedStatus). That does not fit here: a
/// foreign id is excluded by the global query filter before the batch even runs, so the real answer is
/// 404 (entities.Count != ids.Count), not 403. SeedEntityOwnedByOtherUserAsync is left at its default
/// (null) to skip that mismatched assertion rather than let it fail on a correct behavior; the actual
/// cross-user check is BatchDelete_ForeignId_Returns404NotFound below.
/// </summary>
[Collection("Postgres")]
public class ActivityCategoryBatchDeleteTests(AppDbContextFixture fixture) : BaseBatchDeleteEndpointTests(fixture)
{
    protected override string EndpointUrl => "api/activity-category/batch-delete";
    protected override bool IsAdminOnly => false;

    protected override async Task<long[]> SeedEntitiesAsync(DbContext db, int count)
    {
        var ids = new long[count];
        for (var i = 0; i < count; i++)
            ids[i] = await Seed.CategoryAsync(db, FakeLoggedUserService.TestUserId, $"BatchDelete Category {i} {Guid.NewGuid():N}");
        return ids;
    }

    [Fact]
    public async Task BatchDelete_ForeignId_Returns404NotFound()
    {
        var otherUserId = await Seed.SecondUserAsync(Fixture, $"category-batchdelete-{Guid.NewGuid():N}@test.com");
        await using var db = CreateDbContext();
        var foreignId = await Seed.CategoryAsync(db, otherUserId, "Foreign BatchDelete Category");

        var response = await CreateClient().PostAsJsonAsync(EndpointUrl, new { ids = new[] { foreignId } }, JsonOpts);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);

        await using var assertDb = CreateDbContext();
        (await assertDb.Set<ActivityCategory>().IgnoreQueryFilters().AnyAsync(c => c.Id == foreignId, CancellationToken))
            .Should().BeTrue("the foreign row must survive an id it was never scoped to see");
    }
}

// =====================================================================================================
// MemoryAnchor
// =====================================================================================================

[Collection("Postgres")]
public class MemoryAnchorGetByIdTests(AppDbContextFixture fixture) : BaseGetByIdEndpointTests(fixture)
{
    protected override string EndpointUrl => "api/memory-anchor";
    protected override bool IsAdminOnly => false;
    protected override HttpStatusCode UnauthorizedStatus => HttpStatusCode.NotFound;
    protected override Task<long> SeedEntityAsync(DbContext db) => Seed.MemoryAnchorAsync(db, FakeLoggedUserService.TestUserId);

    protected override async Task<long?> SeedEntityOwnedByOtherUserAsync(DbContext db)
    {
        var otherUserId = await Seed.SecondUserAsync(Fixture, $"anchor-getbyid-{Guid.NewGuid():N}@test.com");
        return await Seed.MemoryAnchorAsync(db, otherUserId);
    }
}

[Collection("Postgres")]
public class MemoryAnchorGridTests(AppDbContextFixture fixture) : BaseGridEndpointTests(fixture)
{
    protected override string EndpointUrl => "api/memory-anchor/filtered-table";
    protected override bool IsAdminOnly => false;
    protected override Task<long> SeedEntityAsync(DbContext db) => Seed.MemoryAnchorAsync(db, FakeLoggedUserService.TestUserId);
}

[Collection("Postgres")]
public class MemoryAnchorCreateTests(AppDbContextFixture fixture) : BaseCreateEndpointTests(fixture)
{
    protected override string EndpointUrl => "api/memory-anchor";
    protected override bool IsAdminOnly => false;

    /// <summary>
    /// CreateMemoryAnchorValidator carries a business rule the other create endpoints don't: the target
    /// Activity must already have a Backlog or BucketList profile before it can be anchored. A bare
    /// Activity is rejected with a 400 ("must have a Backlog or BucketList profile"), so the payload
    /// builder has to seed a backlog profile first, not just an Activity.
    /// </summary>
    protected override async Task<object> BuildValidPayloadAsync(DbContext db)
    {
        var userId = FakeLoggedUserService.TestUserId;
        var activityId = await Seed.ActivityAsync(db, userId, $"Anchorable Activity {Guid.NewGuid():N}");
        var locationTypeId = await Seed.LookupAsync<ActivityLocationType>(db, userId, $"Loc {Guid.NewGuid():N}");
        var weatherId = await Seed.LookupAsync<ActivityWeatherDependency>(db, userId, $"Weather {Guid.NewGuid():N}");
        var costTierId = await Seed.LookupAsync<ActivityExpectedCostTier>(db, userId, $"Cost {Guid.NewGuid():N}");
        db.Set<ActivityBacklogProfile>().Add(new ActivityBacklogProfile
        {
            ActivityId = activityId,
            LocationTypeId = locationTypeId,
            WeatherDependencyId = weatherId,
            ExpectedCostTierId = costTierId,
            EnergyLevel = EnergyLevel.Low,
            MinParticipants = 1,
            DurationMinutes = 30
        });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        return new { ActivityId = activityId, AnchorMonth = 3, AnchorYear = 2025, HighlightNote = "Great day", Rating = 8 };
    }
}

[Collection("Postgres")]
public class MemoryAnchorUpdateTests(AppDbContextFixture fixture) : BaseUpdateEndpointTests(fixture)
{
    protected override string EndpointUrl => "api/memory-anchor";
    protected override bool IsAdminOnly => false;
    protected override HttpStatusCode UnauthorizedStatus => HttpStatusCode.NotFound;
    protected override Task<long> SeedEntityAsync(DbContext db) => Seed.MemoryAnchorAsync(db, FakeLoggedUserService.TestUserId);

    protected override async Task<object> BuildValidPayloadAsync(DbContext db, long id)
    {
        var anchor = await db.Set<MemoryAnchor>().AsNoTracking().FirstAsync(a => a.Id == id, TestContext.Current.CancellationToken);
        return new { ActivityId = anchor.ActivityId, AnchorMonth = 7, AnchorYear = 2025, HighlightNote = "Updated note", Rating = 9 };
    }

    protected override async Task<long?> SeedEntityOwnedByOtherUserAsync(DbContext db)
    {
        var otherUserId = await Seed.SecondUserAsync(Fixture, $"anchor-update-{Guid.NewGuid():N}@test.com");
        return await Seed.MemoryAnchorAsync(db, otherUserId);
    }
}

[Collection("Postgres")]
public class MemoryAnchorDeleteTests(AppDbContextFixture fixture) : BaseDeleteEndpointTests(fixture)
{
    protected override string EndpointUrl => "api/memory-anchor";
    protected override bool IsAdminOnly => false;
    protected override HttpStatusCode UnauthorizedStatus => HttpStatusCode.NotFound;
    protected override Task<long> SeedEntityAsync(DbContext db) => Seed.MemoryAnchorAsync(db, FakeLoggedUserService.TestUserId);

    protected override async Task<long?> SeedEntityOwnedByOtherUserAsync(DbContext db)
    {
        var otherUserId = await Seed.SecondUserAsync(Fixture, $"anchor-delete-{Guid.NewGuid():N}@test.com");
        return await Seed.MemoryAnchorAsync(db, otherUserId);
    }
}

/// <summary>
/// This test caught GetSelectOptionsMemoryAnchorEndpoint.Map sitting at `throw new
/// NotImplementedException()` in production code (the base handler swallows it into a 500). Map is
/// implemented now -- MemoryAnchor has no Name, so the option text is HighlightNote.
/// </summary>
[Collection("Postgres")]
public class MemoryAnchorSelectOptionsTests(AppDbContextFixture fixture) : BaseGetSelectOptionsEndpointTests(fixture)
{
    protected override string EndpointUrl => "api/memory-anchor/all-options";
    protected override bool IsAdminOnly => false;
    protected override Task<long> SeedEntityAsync(DbContext db) => Seed.MemoryAnchorAsync(db, FakeLoggedUserService.TestUserId);
}

/// <summary>See <see cref="ActivityCategoryBatchDeleteTests"/> for why SeedEntityOwnedByOtherUserAsync is left null.</summary>
[Collection("Postgres")]
public class MemoryAnchorBatchDeleteTests(AppDbContextFixture fixture) : BaseBatchDeleteEndpointTests(fixture)
{
    protected override string EndpointUrl => "api/memory-anchor/batch-delete";
    protected override bool IsAdminOnly => false;

    protected override async Task<long[]> SeedEntitiesAsync(DbContext db, int count)
    {
        var ids = new long[count];
        for (var i = 0; i < count; i++)
            ids[i] = await Seed.MemoryAnchorAsync(db, FakeLoggedUserService.TestUserId, $"BatchDelete Anchor {i}");
        return ids;
    }
}

// =====================================================================================================
// The four Activity*Lookup entities -- structurally identical endpoints, repeated per type.
// =====================================================================================================

[Collection("Postgres")]
public class ActivityExpectedCostTierGetByIdTests(AppDbContextFixture fixture) : BaseGetByIdEndpointTests(fixture)
{
    protected override string EndpointUrl => "api/activity-expected-cost-tier";
    protected override bool IsAdminOnly => false;
    protected override HttpStatusCode UnauthorizedStatus => HttpStatusCode.NotFound;
    protected override Task<long> SeedEntityAsync(DbContext db) => Seed.LookupAsync<ActivityExpectedCostTier>(db, FakeLoggedUserService.TestUserId, "GetById CostTier");

    protected override async Task<long?> SeedEntityOwnedByOtherUserAsync(DbContext db)
    {
        var otherUserId = await Seed.SecondUserAsync(Fixture, $"costtier-getbyid-{Guid.NewGuid():N}@test.com");
        return await Seed.LookupAsync<ActivityExpectedCostTier>(db, otherUserId, "Foreign CostTier");
    }
}

[Collection("Postgres")]
public class ActivityExpectedCostTierGridTests(AppDbContextFixture fixture) : BaseGridEndpointTests(fixture)
{
    protected override string EndpointUrl => "api/activity-expected-cost-tier/filtered-table";
    protected override bool IsAdminOnly => false;
    protected override Task<long> SeedEntityAsync(DbContext db) => Seed.LookupAsync<ActivityExpectedCostTier>(db, FakeLoggedUserService.TestUserId, "Grid CostTier");
}

[Collection("Postgres")]
public class ActivityExpectedCostTierCreateTests(AppDbContextFixture fixture) : BaseCreateEndpointTests(fixture)
{
    protected override string EndpointUrl => "api/activity-expected-cost-tier";
    protected override bool IsAdminOnly => false;
    protected override Task<object> BuildValidPayloadAsync(DbContext db) =>
        Task.FromResult<object>(new { text = $"Created CostTier {Guid.NewGuid():N}", sortOrder = (int?)null });
}

[Collection("Postgres")]
public class ActivityExpectedCostTierUpdateTests(AppDbContextFixture fixture) : BaseUpdateEndpointTests(fixture)
{
    protected override string EndpointUrl => "api/activity-expected-cost-tier";
    protected override bool IsAdminOnly => false;
    protected override HttpStatusCode UnauthorizedStatus => HttpStatusCode.NotFound;
    protected override Task<long> SeedEntityAsync(DbContext db) => Seed.LookupAsync<ActivityExpectedCostTier>(db, FakeLoggedUserService.TestUserId, "Update CostTier");
    protected override Task<object> BuildValidPayloadAsync(DbContext db, long id) =>
        Task.FromResult<object>(new { text = "Updated CostTier", sortOrder = (int?)1 });

    protected override async Task<long?> SeedEntityOwnedByOtherUserAsync(DbContext db)
    {
        var otherUserId = await Seed.SecondUserAsync(Fixture, $"costtier-update-{Guid.NewGuid():N}@test.com");
        return await Seed.LookupAsync<ActivityExpectedCostTier>(db, otherUserId, "Foreign Update CostTier");
    }
}

// No single Delete<X>Endpoint exists for the four lookups -- only Create, Update and BatchDelete. A
// BaseDeleteEndpointTests subclass here would hit a route FastEndpoints never registers (405), which
// is what an earlier version of this file did before this was caught by actually running the suite.

/// <summary>See <see cref="ActivityCategoryBatchDeleteTests"/> for why SeedEntityOwnedByOtherUserAsync is left null.</summary>
[Collection("Postgres")]
public class ActivityExpectedCostTierBatchDeleteTests(AppDbContextFixture fixture) : BaseBatchDeleteEndpointTests(fixture)
{
    protected override string EndpointUrl => "api/activity-expected-cost-tier/batch-delete";
    protected override bool IsAdminOnly => false;

    protected override async Task<long[]> SeedEntitiesAsync(DbContext db, int count)
    {
        var ids = new long[count];
        for (var i = 0; i < count; i++)
            ids[i] = await Seed.LookupAsync<ActivityExpectedCostTier>(db, FakeLoggedUserService.TestUserId, $"BatchDelete CostTier {i} {Guid.NewGuid():N}");
        return ids;
    }
}

[Collection("Postgres")]
public class ActivityExperienceTypeGetByIdTests(AppDbContextFixture fixture) : BaseGetByIdEndpointTests(fixture)
{
    protected override string EndpointUrl => "api/activity-experience-type";
    protected override bool IsAdminOnly => false;
    protected override HttpStatusCode UnauthorizedStatus => HttpStatusCode.NotFound;
    protected override Task<long> SeedEntityAsync(DbContext db) => Seed.LookupAsync<ActivityExperienceType>(db, FakeLoggedUserService.TestUserId, "GetById ExperienceType");

    protected override async Task<long?> SeedEntityOwnedByOtherUserAsync(DbContext db)
    {
        var otherUserId = await Seed.SecondUserAsync(Fixture, $"exptype-getbyid-{Guid.NewGuid():N}@test.com");
        return await Seed.LookupAsync<ActivityExperienceType>(db, otherUserId, "Foreign ExperienceType");
    }
}

[Collection("Postgres")]
public class ActivityExperienceTypeGridTests(AppDbContextFixture fixture) : BaseGridEndpointTests(fixture)
{
    protected override string EndpointUrl => "api/activity-experience-type/filtered-table";
    protected override bool IsAdminOnly => false;
    protected override Task<long> SeedEntityAsync(DbContext db) => Seed.LookupAsync<ActivityExperienceType>(db, FakeLoggedUserService.TestUserId, "Grid ExperienceType");
}

[Collection("Postgres")]
public class ActivityExperienceTypeCreateTests(AppDbContextFixture fixture) : BaseCreateEndpointTests(fixture)
{
    protected override string EndpointUrl => "api/activity-experience-type";
    protected override bool IsAdminOnly => false;
    protected override Task<object> BuildValidPayloadAsync(DbContext db) =>
        Task.FromResult<object>(new { text = $"Created ExperienceType {Guid.NewGuid():N}", sortOrder = (int?)null });
}

[Collection("Postgres")]
public class ActivityExperienceTypeUpdateTests(AppDbContextFixture fixture) : BaseUpdateEndpointTests(fixture)
{
    protected override string EndpointUrl => "api/activity-experience-type";
    protected override bool IsAdminOnly => false;
    protected override HttpStatusCode UnauthorizedStatus => HttpStatusCode.NotFound;
    protected override Task<long> SeedEntityAsync(DbContext db) => Seed.LookupAsync<ActivityExperienceType>(db, FakeLoggedUserService.TestUserId, "Update ExperienceType");
    protected override Task<object> BuildValidPayloadAsync(DbContext db, long id) =>
        Task.FromResult<object>(new { text = "Updated ExperienceType", sortOrder = (int?)1 });

    protected override async Task<long?> SeedEntityOwnedByOtherUserAsync(DbContext db)
    {
        var otherUserId = await Seed.SecondUserAsync(Fixture, $"exptype-update-{Guid.NewGuid():N}@test.com");
        return await Seed.LookupAsync<ActivityExperienceType>(db, otherUserId, "Foreign Update ExperienceType");
    }
}

// No single Delete<X>Endpoint exists for this lookup either -- see the comment above
// ActivityExpectedCostTierBatchDeleteTests.

/// <summary>See <see cref="ActivityCategoryBatchDeleteTests"/> for why SeedEntityOwnedByOtherUserAsync is left null.</summary>
[Collection("Postgres")]
public class ActivityExperienceTypeBatchDeleteTests(AppDbContextFixture fixture) : BaseBatchDeleteEndpointTests(fixture)
{
    protected override string EndpointUrl => "api/activity-experience-type/batch-delete";
    protected override bool IsAdminOnly => false;

    protected override async Task<long[]> SeedEntitiesAsync(DbContext db, int count)
    {
        var ids = new long[count];
        for (var i = 0; i < count; i++)
            ids[i] = await Seed.LookupAsync<ActivityExperienceType>(db, FakeLoggedUserService.TestUserId, $"BatchDelete ExperienceType {i} {Guid.NewGuid():N}");
        return ids;
    }
}

[Collection("Postgres")]
public class ActivityLocationTypeGetByIdTests(AppDbContextFixture fixture) : BaseGetByIdEndpointTests(fixture)
{
    protected override string EndpointUrl => "api/activity-location-type";
    protected override bool IsAdminOnly => false;
    protected override HttpStatusCode UnauthorizedStatus => HttpStatusCode.NotFound;
    protected override Task<long> SeedEntityAsync(DbContext db) => Seed.LookupAsync<ActivityLocationType>(db, FakeLoggedUserService.TestUserId, "GetById LocationType");

    protected override async Task<long?> SeedEntityOwnedByOtherUserAsync(DbContext db)
    {
        var otherUserId = await Seed.SecondUserAsync(Fixture, $"loctype-getbyid-{Guid.NewGuid():N}@test.com");
        return await Seed.LookupAsync<ActivityLocationType>(db, otherUserId, "Foreign LocationType");
    }
}

[Collection("Postgres")]
public class ActivityLocationTypeGridTests(AppDbContextFixture fixture) : BaseGridEndpointTests(fixture)
{
    protected override string EndpointUrl => "api/activity-location-type/filtered-table";
    protected override bool IsAdminOnly => false;
    protected override Task<long> SeedEntityAsync(DbContext db) => Seed.LookupAsync<ActivityLocationType>(db, FakeLoggedUserService.TestUserId, "Grid LocationType");
}

[Collection("Postgres")]
public class ActivityLocationTypeCreateTests(AppDbContextFixture fixture) : BaseCreateEndpointTests(fixture)
{
    protected override string EndpointUrl => "api/activity-location-type";
    protected override bool IsAdminOnly => false;
    protected override Task<object> BuildValidPayloadAsync(DbContext db) =>
        Task.FromResult<object>(new { text = $"Created LocationType {Guid.NewGuid():N}", sortOrder = (int?)null });
}

[Collection("Postgres")]
public class ActivityLocationTypeUpdateTests(AppDbContextFixture fixture) : BaseUpdateEndpointTests(fixture)
{
    protected override string EndpointUrl => "api/activity-location-type";
    protected override bool IsAdminOnly => false;
    protected override HttpStatusCode UnauthorizedStatus => HttpStatusCode.NotFound;
    protected override Task<long> SeedEntityAsync(DbContext db) => Seed.LookupAsync<ActivityLocationType>(db, FakeLoggedUserService.TestUserId, "Update LocationType");
    protected override Task<object> BuildValidPayloadAsync(DbContext db, long id) =>
        Task.FromResult<object>(new { text = "Updated LocationType", sortOrder = (int?)1 });

    protected override async Task<long?> SeedEntityOwnedByOtherUserAsync(DbContext db)
    {
        var otherUserId = await Seed.SecondUserAsync(Fixture, $"loctype-update-{Guid.NewGuid():N}@test.com");
        return await Seed.LookupAsync<ActivityLocationType>(db, otherUserId, "Foreign Update LocationType");
    }
}

// No single Delete<X>Endpoint exists for this lookup either -- see the comment above
// ActivityExpectedCostTierBatchDeleteTests.

/// <summary>See <see cref="ActivityCategoryBatchDeleteTests"/> for why SeedEntityOwnedByOtherUserAsync is left null.</summary>
[Collection("Postgres")]
public class ActivityLocationTypeBatchDeleteTests(AppDbContextFixture fixture) : BaseBatchDeleteEndpointTests(fixture)
{
    protected override string EndpointUrl => "api/activity-location-type/batch-delete";
    protected override bool IsAdminOnly => false;

    protected override async Task<long[]> SeedEntitiesAsync(DbContext db, int count)
    {
        var ids = new long[count];
        for (var i = 0; i < count; i++)
            ids[i] = await Seed.LookupAsync<ActivityLocationType>(db, FakeLoggedUserService.TestUserId, $"BatchDelete LocationType {i} {Guid.NewGuid():N}");
        return ids;
    }
}

[Collection("Postgres")]
public class ActivityWeatherDependencyGetByIdTests(AppDbContextFixture fixture) : BaseGetByIdEndpointTests(fixture)
{
    protected override string EndpointUrl => "api/activity-weather-dependency";
    protected override bool IsAdminOnly => false;
    protected override HttpStatusCode UnauthorizedStatus => HttpStatusCode.NotFound;
    protected override Task<long> SeedEntityAsync(DbContext db) => Seed.LookupAsync<ActivityWeatherDependency>(db, FakeLoggedUserService.TestUserId, "GetById WeatherDependency");

    protected override async Task<long?> SeedEntityOwnedByOtherUserAsync(DbContext db)
    {
        var otherUserId = await Seed.SecondUserAsync(Fixture, $"weather-getbyid-{Guid.NewGuid():N}@test.com");
        return await Seed.LookupAsync<ActivityWeatherDependency>(db, otherUserId, "Foreign WeatherDependency");
    }
}

[Collection("Postgres")]
public class ActivityWeatherDependencyGridTests(AppDbContextFixture fixture) : BaseGridEndpointTests(fixture)
{
    protected override string EndpointUrl => "api/activity-weather-dependency/filtered-table";
    protected override bool IsAdminOnly => false;
    protected override Task<long> SeedEntityAsync(DbContext db) => Seed.LookupAsync<ActivityWeatherDependency>(db, FakeLoggedUserService.TestUserId, "Grid WeatherDependency");
}

[Collection("Postgres")]
public class ActivityWeatherDependencyCreateTests(AppDbContextFixture fixture) : BaseCreateEndpointTests(fixture)
{
    protected override string EndpointUrl => "api/activity-weather-dependency";
    protected override bool IsAdminOnly => false;
    protected override Task<object> BuildValidPayloadAsync(DbContext db) =>
        Task.FromResult<object>(new { text = $"Created WeatherDependency {Guid.NewGuid():N}", sortOrder = (int?)null });
}

[Collection("Postgres")]
public class ActivityWeatherDependencyUpdateTests(AppDbContextFixture fixture) : BaseUpdateEndpointTests(fixture)
{
    protected override string EndpointUrl => "api/activity-weather-dependency";
    protected override bool IsAdminOnly => false;
    protected override HttpStatusCode UnauthorizedStatus => HttpStatusCode.NotFound;
    protected override Task<long> SeedEntityAsync(DbContext db) => Seed.LookupAsync<ActivityWeatherDependency>(db, FakeLoggedUserService.TestUserId, "Update WeatherDependency");
    protected override Task<object> BuildValidPayloadAsync(DbContext db, long id) =>
        Task.FromResult<object>(new { text = "Updated WeatherDependency", sortOrder = (int?)1 });

    protected override async Task<long?> SeedEntityOwnedByOtherUserAsync(DbContext db)
    {
        var otherUserId = await Seed.SecondUserAsync(Fixture, $"weather-update-{Guid.NewGuid():N}@test.com");
        return await Seed.LookupAsync<ActivityWeatherDependency>(db, otherUserId, "Foreign Update WeatherDependency");
    }
}

// No single Delete<X>Endpoint exists for this lookup either -- see the comment above
// ActivityExpectedCostTierBatchDeleteTests.

/// <summary>See <see cref="ActivityCategoryBatchDeleteTests"/> for why SeedEntityOwnedByOtherUserAsync is left null.</summary>
[Collection("Postgres")]
public class ActivityWeatherDependencyBatchDeleteTests(AppDbContextFixture fixture) : BaseBatchDeleteEndpointTests(fixture)
{
    protected override string EndpointUrl => "api/activity-weather-dependency/batch-delete";
    protected override bool IsAdminOnly => false;

    protected override async Task<long[]> SeedEntitiesAsync(DbContext db, int count)
    {
        var ids = new long[count];
        for (var i = 0; i < count; i++)
            ids[i] = await Seed.LookupAsync<ActivityWeatherDependency>(db, FakeLoggedUserService.TestUserId, $"BatchDelete WeatherDependency {i} {Guid.NewGuid():N}");
        return ids;
    }
}

// =====================================================================================================
// D: uniqueness invariants, E: delete behavior, F: Clone, G: QuickEdit -- hand-written, none of the
// 13 framework bases cover uniqueness conflicts, cascade behavior or these two bespoke endpoints.
// =====================================================================================================

[Collection("Postgres")]
public class ActivityAreaBehaviorTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    // ---- D: uniqueness -------------------------------------------------------------------------

    [Fact]
    public async Task Activity_DuplicateNameSameUser_Returns409NotRawServerError()
    {
        await using var db = CreateDbContext();
        var roleId = await Seed.RoleAsync(db, FakeLoggedUserService.TestUserId, $"Dup Role {Guid.NewGuid():N}");
        var payload = new { Name = "Duplicate Activity", IsUnavoidable = false, RoleId = roleId };

        var first = await CreateClient().PostAsJsonAsync("api/activity", payload, JsonOpts);
        first.StatusCode.Should().Be(HttpStatusCode.Created);

        var second = await CreateClient().PostAsJsonAsync("api/activity", payload, JsonOpts);

        second.StatusCode.Should().Be(HttpStatusCode.Conflict, "a (UserId, Name) unique violation must map to 409, not a raw 500");
    }

    [Fact]
    public async Task Activity_SameNameDifferentUser_Succeeds()
    {
        await using var db = CreateDbContext();
        await Seed.ActivityAsync(db, FakeLoggedUserService.TestUserId, "Shared Name Activity");

        var otherUserId = await Seed.SecondUserAsync(Fixture, $"activity-samename-{Guid.NewGuid():N}@test.com");
        var otherRoleId = await Seed.RoleAsync(db, otherUserId, $"Other Role {Guid.NewGuid():N}");

        await using var factoryB = CreateFactory(["User"], otherUserId);
        var response = await factoryB.CreateClient()
            .PostAsJsonAsync("api/activity", new { Name = "Shared Name Activity", IsUnavoidable = false, RoleId = otherRoleId }, JsonOpts);

        response.StatusCode.Should().Be(HttpStatusCode.Created, "the unique index is per-user, so a different user may reuse the name");
    }

    [Fact]
    public async Task ActivityRole_DuplicateNameSameUser_Returns409()
    {
        var payload = new { Name = "Duplicate Role", Color = "#abcdef" };
        var first = await CreateClient().PostAsJsonAsync("api/activity-role", payload, JsonOpts);
        first.StatusCode.Should().Be(HttpStatusCode.Created);

        var second = await CreateClient().PostAsJsonAsync("api/activity-role", payload, JsonOpts);

        second.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task ActivityRole_SameNameDifferentUser_Succeeds()
    {
        await using var db = CreateDbContext();
        await Seed.RoleAsync(db, FakeLoggedUserService.TestUserId, "Shared Name Role");
        var otherUserId = await Seed.SecondUserAsync(Fixture, $"role-samename-{Guid.NewGuid():N}@test.com");

        await using var factoryB = CreateFactory(["User"], otherUserId);
        var response = await factoryB.CreateClient()
            .PostAsJsonAsync("api/activity-role", new { Name = "Shared Name Role", Color = "#123456" }, JsonOpts);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task ActivityCategory_DuplicateNameSameUser_Returns409()
    {
        var payload = new { Name = "Duplicate Category", Color = "#abcdef" };
        var first = await CreateClient().PostAsJsonAsync("api/activity-category", payload, JsonOpts);
        first.StatusCode.Should().Be(HttpStatusCode.Created);

        var second = await CreateClient().PostAsJsonAsync("api/activity-category", payload, JsonOpts);

        second.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task ActivityCategory_SameNameDifferentUser_Succeeds()
    {
        await using var db = CreateDbContext();
        await Seed.CategoryAsync(db, FakeLoggedUserService.TestUserId, "Shared Name Category");
        var otherUserId = await Seed.SecondUserAsync(Fixture, $"category-samename-{Guid.NewGuid():N}@test.com");

        await using var factoryB = CreateFactory(["User"], otherUserId);
        var response = await factoryB.CreateClient()
            .PostAsJsonAsync("api/activity-category", new { Name = "Shared Name Category", Color = "#123456" }, JsonOpts);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    // ---- E: delete behavior + required/optional FKs ---------------------------------------------

    [Fact]
    public async Task Create_WithoutRole_Returns400()
    {
        var response = await CreateClient()
            .PostAsJsonAsync("api/activity", new { Name = "Roleless Activity", IsUnavoidable = false, RoleId = 0L }, JsonOpts);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest, "ActivityValidator requires RoleId to be non-empty");
    }

    /// <summary>
    /// Corrects a stale claim in docs/domain-map.md / review/portal/testingPrompts/ActivityProfileGrids.md
    /// (section E), which says Activity -&gt; PlannerTask is Restrict. It is not: PlannerTaskConfiguration
    /// calls the parameterless IsManyWithOneActivity(), whose default delete behavior is Cascade. This
    /// pins the actual behavior so a future change to Restrict (matching the stale doc) is a deliberate,
    /// visible decision rather than an accidental read of an out-of-date spec.
    /// </summary>
    [Fact]
    public async Task DeletingActivity_CascadesToItsPlannerTasks_NotRestrict()
    {
        await using var db = CreateDbContext();
        var userId = FakeLoggedUserService.TestUserId;
        var activityId = await Seed.ActivityAsync(db, userId, "Activity With PlannerTask");

        var calendar = new Calendar
        {
            UserId = userId,
            Date = DateOnly.FromDateTime(DateTime.UtcNow),
            DayType = DayType.Workday,
            WakeUpTime = new TimeOnly(7, 0),
            BedTime = new TimeOnly(23, 0)
        };
        db.Set<Calendar>().Add(calendar);
        await db.SaveChangesAsync(CancellationToken);

        var task = new PlannerTask
        {
            UserId = userId,
            ActivityId = activityId,
            CalendarId = calendar.Id,
            StartTime = new TimeOnly(9, 0),
            EndTime = new TimeOnly(9, 30),
            IsBackground = false,
            Status = PlannerTaskStatus.NotStarted
        };
        db.Set<PlannerTask>().Add(task);
        await db.SaveChangesAsync(CancellationToken);
        var taskId = task.Id;

        var response = await CreateClient().DeleteAsync($"api/activity/{activityId}");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent, "the FK is Cascade, not Restrict, so the delete must succeed");

        await using var assertDb = CreateDbContext();
        (await assertDb.Set<PlannerTask>().IgnoreQueryFilters().AnyAsync(t => t.Id == taskId, CancellationToken))
            .Should().BeFalse("the planner task must be cascade-deleted along with its activity");
    }

    // ---- F: CloneActivityEndpoint / Activity.Clone (CQ-17) --------------------------------------

    [Fact]
    public async Task Clone_CreatesNewRowAndLeavesSourceAndItsChildrenUntouched()
    {
        await using var db = CreateDbContext();
        var userId = FakeLoggedUserService.TestUserId;
        var roleId = await Seed.RoleAsync(db, userId, $"Clone Role {Guid.NewGuid():N}");
        var sourceActivity = new Activity { UserId = userId, Name = "Source Activity", RoleId = roleId };
        db.Set<Activity>().Add(sourceActivity);
        await db.SaveChangesAsync(CancellationToken);
        var sourceId = sourceActivity.Id;

        var calendar = new Calendar
        {
            UserId = userId,
            Date = DateOnly.FromDateTime(DateTime.UtcNow),
            DayType = DayType.Workday,
            WakeUpTime = new TimeOnly(7, 0),
            BedTime = new TimeOnly(23, 0)
        };
        db.Set<Calendar>().Add(calendar);
        await db.SaveChangesAsync(CancellationToken);
        var childTask = new PlannerTask
        {
            UserId = userId,
            ActivityId = sourceId,
            CalendarId = calendar.Id,
            StartTime = new TimeOnly(10, 0),
            EndTime = new TimeOnly(10, 30),
            IsBackground = false,
            Status = PlannerTaskStatus.NotStarted
        };
        db.Set<PlannerTask>().Add(childTask);
        await db.SaveChangesAsync(CancellationToken);

        var response = await CreateClient()
            .PostAsJsonAsync($"api/activity/{sourceId}/clone", new { Name = "Cloned Activity", Text = (string?)null, CategoryId = (long?)null }, JsonOpts);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var cloneId = await response.Content.ReadFromJsonAsync<long>(CancellationToken);
        cloneId.Should().NotBe(sourceId).And.BeGreaterThan(0);

        await using var assertDb = CreateDbContext();
        var source = await assertDb.Set<Activity>().AsNoTracking().FirstAsync(a => a.Id == sourceId, CancellationToken);
        source.Name.Should().Be("Source Activity", "cloning must not mutate the source row");

        var clone = await assertDb.Set<Activity>().AsNoTracking().FirstAsync(a => a.Id == cloneId, CancellationToken);
        clone.Name.Should().Be("Cloned Activity");
        clone.RoleId.Should().Be(roleId, "clone always keeps the source's RoleId -- QuickEditActivityRequest carries no RoleId field");
        clone.UserId.Should().Be(userId, "the clone must be stamped with the caller's UserId, not blindly copied from MemberwiseClone");

        var tasksForClone = await assertDb.Set<PlannerTask>().IgnoreQueryFilters().Where(t => t.ActivityId == cloneId).ToListAsync(CancellationToken);
        tasksForClone.Should().BeEmpty("Activity carries no navigation collections, so MemberwiseClone cannot have copied any children");

        var taskForSource = await assertDb.Set<PlannerTask>().IgnoreQueryFilters().SingleAsync(t => t.ActivityId == sourceId, CancellationToken);
        taskForSource.Id.Should().Be(childTask.Id, "the source's own child must still point at the source, untouched by the clone");
    }

    [Fact]
    public async Task Clone_WhenRequestNameMatchesSource_AppendsCopySuffix()
    {
        await using var db = CreateDbContext();
        var userId = FakeLoggedUserService.TestUserId;
        var activityId = await Seed.ActivityAsync(db, userId, "Same Name Activity");

        var response = await CreateClient()
            .PostAsJsonAsync($"api/activity/{activityId}/clone", new { Name = "Same Name Activity", Text = (string?)null, CategoryId = (long?)null }, JsonOpts);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var cloneId = await response.Content.ReadFromJsonAsync<long>(CancellationToken);

        await using var assertDb = CreateDbContext();
        var clone = await assertDb.Set<Activity>().AsNoTracking().FirstAsync(a => a.Id == cloneId, CancellationToken);
        clone.Name.Should().Be("Same Name Activity (copy)");
    }

    [Fact]
    public async Task Clone_ForeignActivityId_Returns404()
    {
        var otherUserId = await Seed.SecondUserAsync(Fixture, $"clone-idor-{Guid.NewGuid():N}@test.com");
        await using var db = CreateDbContext();
        var foreignId = await Seed.ActivityAsync(db, otherUserId, "Foreign Clone Source");

        var response = await CreateClient()
            .PostAsJsonAsync($"api/activity/{foreignId}/clone", new { Name = "Stolen Clone", Text = (string?)null, CategoryId = (long?)null }, JsonOpts);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ---- G: QuickEditActivityEndpoint ------------------------------------------------------------

    [Fact]
    public async Task QuickEdit_UpdatesEditableFieldsOnly_RoleIdAndUserIdUnchanged()
    {
        await using var db = CreateDbContext();
        var userId = FakeLoggedUserService.TestUserId;
        var roleId = await Seed.RoleAsync(db, userId, $"QuickEdit Role {Guid.NewGuid():N}");
        var categoryId = await Seed.CategoryAsync(db, userId, $"QuickEdit Category {Guid.NewGuid():N}");
        var activity = new Activity { UserId = userId, Name = "Original Name", RoleId = roleId };
        db.Set<Activity>().Add(activity);
        await db.SaveChangesAsync(CancellationToken);

        var response = await CreateClient()
            .PutAsJsonAsync($"api/activity/{activity.Id}/quick-edit", new { Name = "Edited Name", Text = "New text", CategoryId = categoryId }, JsonOpts);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        await using var assertDb = CreateDbContext();
        var updated = await assertDb.Set<Activity>().AsNoTracking().FirstAsync(a => a.Id == activity.Id, CancellationToken);
        updated.Name.Should().Be("Edited Name");
        updated.Text.Should().Be("New text");
        updated.CategoryId.Should().Be(categoryId);
        updated.RoleId.Should().Be(roleId, "QuickEditActivityRequest carries no RoleId field, so it must be impossible to change here");
        updated.UserId.Should().Be(userId, "QuickEditActivityRequest carries no UserId field -- mass assignment is not possible by DTO shape alone");
    }

    [Fact]
    public async Task QuickEdit_ForeignActivityId_Returns404()
    {
        var otherUserId = await Seed.SecondUserAsync(Fixture, $"quickedit-idor-{Guid.NewGuid():N}@test.com");
        await using var db = CreateDbContext();
        var foreignId = await Seed.ActivityAsync(db, otherUserId, "Foreign QuickEdit Target");

        var response = await CreateClient()
            .PutAsJsonAsync($"api/activity/{foreignId}/quick-edit", new { Name = "Hijacked", Text = (string?)null, CategoryId = (long?)null }, JsonOpts);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);

        await using var assertDb = CreateDbContext();
        var stillThere = await assertDb.Set<Activity>().IgnoreQueryFilters().AsNoTracking().FirstAsync(a => a.Id == foreignId, CancellationToken);
        stillThere.Name.Should().Be("Foreign QuickEdit Target");
    }
}

// =====================================================================================================
// C: DenyExtensionClients -- every endpoint in this area is auto-wired to that policy in Program.cs's
// global Configurator (none carry [AllowExtensionClients]), so an extension-flavored access token must
// be refused everywhere. One representative route proves the wiring; ExtensionActivityTrackingTests
// already proves the opposite direction (that [AllowExtensionClients] surface accepts one).
// =====================================================================================================

[Collection("Postgres")]
public class ActivityExtensionClientDenialTests(AppDbContextFixture fixture) : AuthTestBase(fixture)
{
    private const string Password = "Test@1234!";

    [Fact]
    public async Task ExtensionToken_CannotAccess_ActivityEndpoint_Returns403()
    {
        var email = $"activity-extension-denial-{Guid.NewGuid():N}@test.com";
        await CreateUserWithExtensionAccessAsync(email);

        using var scope = Fixture.UnauthenticatedFactory.Services.CreateScope();
        using var loginClient = CreateCookieClient();
        using var loginRequest = new HttpRequestMessage(HttpMethod.Post, "auth/extension/login");
        loginRequest.Headers.Add("X-Forwarded-For", Guid.NewGuid().ToString());
        loginRequest.Content = JsonContent.Create(new { Email = email, Password });
        var loginResponse = await loginClient.SendAsync(loginRequest);
        loginResponse.EnsureSuccessStatusCode();
        var tokens = await loginResponse.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        var accessToken = tokens.GetProperty("accessToken").GetString();

        using var extensionClient = CreateCookieClient();
        extensionClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        var response = await extensionClient.GetAsync("activity");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden, "DenyExtensionClients is attached to every endpoint that doesn't carry [AllowExtensionClients]");
    }

    private async Task CreateUserWithExtensionAccessAsync(string email)
    {
        using var scope = Fixture.UnauthenticatedFactory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<UserRole>>();

        var user = new User
        {
            Email = email,
            UserName = email,
            EmailConfirmed = true,
            HasExtensionAccess = true,
            Locale = AvailableLocales.En,
            Timezone = TimeZoneInfo.Utc
        };
        await userManager.CreateAsync(user, Password);

        if (await roleManager.FindByNameAsync("User") == null)
            await roleManager.CreateAsync(new UserRole { Name = "User", Description = "User", IsDefault = false, RoleLevel = 1, IsAssignable = true });
        await userManager.AddToRoleAsync(user, "User");
    }
}
