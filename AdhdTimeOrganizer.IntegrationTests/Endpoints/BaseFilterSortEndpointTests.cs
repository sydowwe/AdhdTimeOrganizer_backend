using AdhdTimeOrganizer.domain.model.entity.todoList;
using AdhdTimeOrganizer.infrastructure.persistence;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.Testing;
using Sydowwe.Framework.Testing.baseTests;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Endpoints;

// Tests BaseFilterSortEndpoint via FilterSortTodoListEndpoint ("/todo-list/filter-sort")
[Collection("Postgres")]
public class FilterSortTodoListEndpointTests(AppDbContextFixture fixture)
    : BaseFilterSortEndpointTests(fixture)
{
    protected override string EndpointUrl => "api/todo-list/filter-sort";

    protected override async Task<long> SeedEntityAsync(DbContext db)
    {
        var todoList = new TodoList { Name = "List Alpha", UserId = FakeLoggedUserService.TestUserId };
        ((AppDbContext)db).TodoLists.Add(todoList);
        await db.SaveChangesAsync();
        return todoList.Id;
    }
}