using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.Testing.baseTests;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Endpoints;

[Collection("Postgres")]
public class CreateActivityCategoryEndpointTests(AppDbContextFixture fixture)
    : BaseCreateEndpointTests(fixture)
{
    protected override string EndpointUrl => "api/activity-category";

    protected override Task<object> BuildValidPayloadAsync(DbContext db) => Task.FromResult<object>(new { Name = "Test Category", Color = "#FF0000", Text = "desc" });
}