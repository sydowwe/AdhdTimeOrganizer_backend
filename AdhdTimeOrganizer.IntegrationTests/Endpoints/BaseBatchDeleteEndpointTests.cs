using AdhdTimeOrganizer.domain.model.entity.activity;
using AdhdTimeOrganizer.infrastructure.persistence;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.Testing;
using Sydowwe.Framework.Testing.baseTests;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Endpoints;

[Collection("Postgres")]
public class BatchDeleteActivityCategoryEndpointTests(AppDbContextFixture fixture)
    : BaseBatchDeleteEndpointTests(fixture)
{
    protected override string EndpointUrl => "api/activity-category/batch-delete";

    protected override async Task<long[]> SeedEntitiesAsync(DbContext db, int count)
    {
        var categories = Enumerable.Range(1, count)
            .Select(i => new ActivityCategory { Name = $"Batch {i}", Color = "#FF0000", UserId = FakeLoggedUserService.TestUserId })
            .ToList();
        ((AppDbContext)db).ActivityCategories.AddRange(categories);
        await db.SaveChangesAsync();
        return categories.Select(c => c.Id).ToArray();
    }
}