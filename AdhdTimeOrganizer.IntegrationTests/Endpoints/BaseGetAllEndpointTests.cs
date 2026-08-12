using AdhdTimeOrganizer.Core.domain.model.entity.activity;
using AdhdTimeOrganizer.infrastructure.persistence;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.Testing;
using Sydowwe.Framework.Testing.baseTests;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Endpoints;

[Collection("Postgres")]
public class GetAllActivityCategoryEndpointTests(AppDbContextFixture fixture)
    : BaseGetAllEndpointTests(fixture)
{
    protected override string EndpointUrl => "api/activity-category";

    // ActivityCategory is user-owned data; the endpoint does not override AllowedRoles(), so plain
    // Users can reach it.
    protected override bool IsAdminOnly => false;

    protected override async Task<long> SeedEntityAsync(DbContext db)
    {
        var category = new ActivityCategory { Name = "Cat A", Color = "#FF0000", UserId = FakeLoggedUserService.TestUserId };
        ((AppDbContext)db).ActivityCategories.Add(category);
        await db.SaveChangesAsync();
        return category.Id;
    }
}