using AdhdTimeOrganizer.domain.model.entity.activity;
using AdhdTimeOrganizer.infrastructure.persistence;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.Testing;
using Sydowwe.Framework.Testing.baseTests;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Endpoints;

[Collection("Postgres")]
public class GetByIdActivityCategoryEndpointTests(AppDbContextFixture fixture)
    : BaseGetByIdEndpointTests(fixture)
{
    protected override string EndpointUrl => "api/activity-category";

    protected override async Task<long> SeedEntityAsync(DbContext db)
    {
        var category = new ActivityCategory { Name = "GetById Test", Color = "#FF0000", UserId = FakeLoggedUserService.TestUserId };
        ((AppDbContext)db).ActivityCategories.Add(category);
        await db.SaveChangesAsync();
        return category.Id;
    }
}