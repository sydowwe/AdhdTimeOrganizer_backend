using AdhdTimeOrganizer.Core.domain.model.entity.activity;
using AdhdTimeOrganizer.infrastructure.persistence;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.Testing;
using Sydowwe.Framework.Testing.baseTests;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Endpoints;

[Collection("Postgres")]
public class UpdateActivityCategoryEndpointTests(AppDbContextFixture fixture)
    : BaseUpdateEndpointTests(fixture)
{
    protected override string EndpointUrl => "api/activity-category";

    protected override async Task<long> SeedEntityAsync(DbContext db)
    {
        var category = new ActivityCategory { Name = "Update Test", Color = "#0000FF", UserId = FakeLoggedUserService.TestUserId };
        ((AppDbContext)db).ActivityCategories.Add(category);
        await db.SaveChangesAsync();
        return category.Id;
    }

    protected override Task<object> BuildValidPayloadAsync(DbContext db, long id) => Task.FromResult<object>(new { Name = "Updated", Color = "#00FF00", Text = "updated" });
}