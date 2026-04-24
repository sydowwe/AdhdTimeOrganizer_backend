using System.Net;
using System.Net.Http.Json;
using AdhdTimeOrganizer.application.dto.response.toDoList;
using AdhdTimeOrganizer.domain.model.entity.todoList;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using FluentAssertions;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Endpoints;

// Tests BaseFilterSortEndpoint via GetFilterSortTodoListEndpoint
public class BaseFilterSortEndpointTests(TestWebApplicationFactory factory) : IntegrationTestBase(factory)
{
    private const string Route = "/api/todo-list/filter-sort";

    private async Task SeedTodoListsAsync()
    {
        var db = CreateDbContext();
        var userId = await GetTestUserIdAsync();
        db.TodoLists.AddRange(
            new TodoList { Name = "List Alpha", UserId = userId },
            new TodoList { Name = "List Beta", UserId = userId });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task FilterSort_ValidRequest_Returns200WithList()
    {
        await SeedTodoListsAsync();
        var request = new
        {
            SortBy = Array.Empty<object>(),
            UseFilter = false,
            Filter = new { }
        };

        var response = await Client.PostAsJsonAsync(Route, request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var items = await response.Content.ReadFromJsonAsync<List<TodoListResponse>>();
        items.Should().NotBeNull();
        items!.Count.Should().BeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public async Task FilterSort_WithFilter_Returns200WithFilteredList()
    {
        await SeedTodoListsAsync();
        var request = new
        {
            SortBy = Array.Empty<object>(),
            UseFilter = true,
            Filter = new { Name = "Alpha" }
        };

        var response = await Client.PostAsJsonAsync(Route, request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var items = await response.Content.ReadFromJsonAsync<List<TodoListResponse>>();
        items.Should().NotBeNull();
        items!.Should().OnlyContain(i => i.Name.Contains("Alpha"));
    }

    [Fact]
    public async Task FilterSort_WithoutAuth_Returns401()
    {
        using var anonClient = Factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            HandleCookies = false,
            AllowAutoRedirect = false
        });

        var response = await anonClient.PostAsJsonAsync(Route, new { SortBy = Array.Empty<object>(), UseFilter = false, Filter = new { } });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    public override async Task DisposeAsync()
    {
        var db = CreateDbContext();
        var userId = await GetTestUserIdAsync();
        db.TodoLists.RemoveRange(
            db.TodoLists.Where(t => t.UserId == userId));
        await db.SaveChangesAsync();
    }
}
