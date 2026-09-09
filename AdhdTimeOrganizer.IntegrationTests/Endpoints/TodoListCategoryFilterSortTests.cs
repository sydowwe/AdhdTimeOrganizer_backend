using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using AdhdTimeOrganizer.TodoLists.application.dto.response.todoList;
using FluentAssertions;
using Sydowwe.Framework.Testing;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Endpoints;

/// <summary>
/// TEST-6 — the arms of <c>POST /api/todo-list-category/filter-sort</c> that
/// <see cref="FilterSortTodoListCategoryTests"/> (in <c>TodoListReadSurfaceTests.cs</c>) does not reach:
/// the synthetic row's literal field values, its position relative to sorting, the
/// <c>UseFilter</c>/<c>HideEmpty</c> combination the base class's pattern match decides, and the
/// round trip that ties the sentinel's producer to its consumer.
/// <para>
/// Everything here fails <b>silently</b>. The "Other" row is not a database row — no migration, no
/// model configuration and no FK pins it — so a wrong <c>Icon</c>, a row that moved ahead of the sort,
/// or a sentinel that drifted apart from <c>FilterSortTodoListEndpoint</c>'s reading of it all answer
/// 200 with a body the frontend renders wrong. That last one is the point of
/// <see cref="SentinelRoundTripTests"/>: <see cref="TodoListCategoryResponse.UncategorizedId"/> is
/// produced in one endpoint and consumed in another, and no compiler check connects them.
/// </para>
/// <para>
/// This file seeds no <c>TaskPriority</c>, so it claims no rank band from the
/// <c>(UserId, Priority)</c> index that the rest of the collection shares.
/// </para>
/// </summary>
[Collection("Postgres")]
public class TodoListCategoryFilterSortTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    /// <summary>
    /// The frontend renders <c>Color</c> and <c>Icon</c> straight through, so a typo in either is
    /// invisible server-side — the response is still a well-formed 200. Asserting the literals, rather
    /// than comparing against <see cref="TodoListCategoryResponse.Uncategorized"/>, is deliberate: a
    /// test that reads the constant back would follow the typo.
    /// </summary>
    [Fact(DisplayName = "The 'Other' row carries the exact Id/Name/Color/Icon/Text the frontend renders")]
    public async Task OtherRow_HasTheExactLiteralFields()
    {
        await using (var db = CreateDbContext())
        {
            await TodoListTestSeedHelper.SeedTodoListCategoryAsync(db, name: "Chores", ct: CancellationToken);
            await TodoListTestSeedHelper.SeedTodoListCategoryAsync(db, name: "Work", ct: CancellationToken);
            await TodoListTestSeedHelper.SeedTodoListAsync(db, name: "Loose list", ct: CancellationToken);
        }

        var categories = await FilterSortAsync();

        categories.Should().HaveCount(3, "two real categories plus the synthetic row");

        var other = categories.Should().ContainSingle(c => c.Id == -1).Subject;

        other.Id.Should().Be(-1);
        other.Name.Should().Be("Other");
        other.Color.Should().Be("default");
        other.Icon.Should().Be("fas fa-ellipsis");
        other.Text.Should().BeNull();
    }

    /// <summary>
    /// The row is appended by <c>PostProcessItemsAsync</c>, which <c>BaseFilterSortEndpoint</c> runs
    /// <i>after</i> <c>SortByMany</c> — so it lands last whatever the sort says, and never sorts into
    /// place under "O". Both directions are asserted because a single ascending case would also pass if
    /// the row happened to sort correctly.
    /// </summary>
    [Fact(DisplayName = "The 'Other' row is appended after sorting, so it is last under both sort directions")]
    public async Task OtherRow_IsAppendedAfterSorting()
    {
        await using (var db = CreateDbContext())
        {
            await TodoListTestSeedHelper.SeedTodoListCategoryAsync(db, name: "Zebra", ct: CancellationToken);
            await TodoListTestSeedHelper.SeedTodoListCategoryAsync(db, name: "Alpha", ct: CancellationToken);
            await TodoListTestSeedHelper.SeedTodoListAsync(db, name: "Loose list", ct: CancellationToken);
        }

        var ascending = await FilterSortAsync(sortBy: [new { Key = "name", IsDesc = false }]);
        ascending.Select(c => c.Name).Should().Equal("Alpha", "Zebra", "Other");

        var descending = await FilterSortAsync(sortBy: [new { Key = "name", IsDesc = true }]);
        descending.Select(c => c.Name).Should().Equal("Zebra", "Alpha", "Other");
    }

    /// <summary>
    /// <c>BaseFilterSortEndpoint</c> guards <c>ApplyCustomFiltering</c> with
    /// <c>req is { UseFilter: true, Filter: not null }</c>, so a <c>HideEmpty</c> sent alongside
    /// <c>UseFilter = false</c> must do nothing at all. This is the arm a rewrite of that pattern match
    /// would break, and the one no other test in the collection sends.
    /// </summary>
    [Fact(DisplayName = "HideEmpty = true with UseFilter = false filters nothing")]
    public async Task HideEmpty_IsIgnoredWhenUseFilterIsFalse()
    {
        await using (var db = CreateDbContext())
        {
            var used = await TodoListTestSeedHelper.SeedTodoListCategoryAsync(db, name: "Chores", ct: CancellationToken);
            await TodoListTestSeedHelper.SeedTodoListCategoryAsync(db, name: "Empty", ct: CancellationToken);
            await TodoListTestSeedHelper.SeedTodoListAsync(db, name: "Dishes", categoryId: used, ct: CancellationToken);
        }

        var unfiltered = await FilterSortAsync(useFilter: false, filter: new { HideEmpty = true });
        unfiltered.Select(c => c.Name).Should().BeEquivalentTo(["Chores", "Empty"]);

        var filtered = await FilterSortAsync(useFilter: true, filter: new { HideEmpty = true });
        filtered.Select(c => c.Name).Should().BeEquivalentTo(["Chores"]);
    }

    /// <summary>
    /// Unstated but intended: the "Other" probe in <c>PostProcessItemsAsync</c> is a separate query that
    /// never sees <c>HideEmpty</c>, so the bucket survives a filter that empties the real list. It is
    /// defensible — "Other" only exists when an uncategorised list does, so it is never empty — but
    /// nothing says so anywhere, which is why it is pinned here.
    /// </summary>
    [Fact(DisplayName = "HideEmpty does not suppress the 'Other' row -- the probe is a separate query that never sees the filter")]
    public async Task HideEmpty_DoesNotSuppressTheOtherRow()
    {
        await using (var db = CreateDbContext())
        {
            await TodoListTestSeedHelper.SeedTodoListCategoryAsync(db, name: "Empty", ct: CancellationToken);
            await TodoListTestSeedHelper.SeedTodoListAsync(db, name: "Loose list", ct: CancellationToken);
        }

        var categories = await FilterSortAsync(useFilter: true, filter: new { HideEmpty = true });

        categories.Select(c => c.Id).Should()
            .Equal([TodoListCategoryResponse.UncategorizedId], "every real category was filtered out, and only the synthetic row is left");
    }

    /// <summary>
    /// The same guard as <see cref="HideEmpty_IsIgnoredWhenUseFilterIsFalse"/>, from the other side:
    /// <c>Filter</c> is nullable, so <c>UseFilter = true</c> with no filter body must fall through to
    /// the unfiltered list rather than dereference it.
    /// </summary>
    [Fact(DisplayName = "UseFilter = true with a null Filter is 200 and the full list, not a 500")]
    public async Task UseFilterTrue_WithNullFilter_ReturnsEverything()
    {
        await using (var db = CreateDbContext())
        {
            await TodoListTestSeedHelper.SeedTodoListCategoryAsync(db, name: "Chores", ct: CancellationToken);
            await TodoListTestSeedHelper.SeedTodoListCategoryAsync(db, name: "Work", ct: CancellationToken);
        }

        var categories = await FilterSortAsync(useFilter: true, filter: null);

        categories.Select(c => c.Name).Should().BeEquivalentTo(["Chores", "Work"]);
    }

    /// <summary>
    /// The route takes the default <c>AllowedRoles()</c> (User + Admin + Root) from
    /// <c>BaseFilterSortEndpoint</c>. The rest of the collection exercises it as Admin+User; this pins
    /// that a plain User is not incidentally locked out.
    /// </summary>
    [Fact(DisplayName = "A User-role client reaches the route -- the default role set is not admin-only")]
    public async Task UserRoleClient_Gets200()
    {
        await using (var db = CreateDbContext())
            await TodoListTestSeedHelper.SeedTodoListCategoryAsync(db, name: "Chores", ct: CancellationToken);

        var response = await CreateUserRoleClient().PostAsJsonAsync("api/todo-list-category/filter-sort", new
        {
            SortBy = Array.Empty<object>(),
            UseFilter = false,
            Filter = (object?)null
        }, JsonOpts, CancellationToken);

        var body = await response.Content.ReadAsStringAsync(CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);

        JsonSerializer.Deserialize<List<TodoListCategoryResponse>>(body, JsonOpts)!
            .Select(c => c.Name).Should().BeEquivalentTo(["Chores"]);
    }

    private async Task<List<TodoListCategoryResponse>> FilterSortAsync(
        bool useFilter = false, object? filter = null, object[]? sortBy = null)
    {
        var response = await CreateClient().PostAsJsonAsync("api/todo-list-category/filter-sort", new
        {
            SortBy = sortBy ?? [],
            UseFilter = useFilter,
            Filter = filter
        }, JsonOpts, CancellationToken);

        var body = await response.Content.ReadAsStringAsync(CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);

        return JsonSerializer.Deserialize<List<TodoListCategoryResponse>>(body, JsonOpts)!;
    }
}

/// <summary>
/// TEST-6 scenario 10 — the only test that ties the two halves of the <c>-1</c> sentinel together.
/// <c>GetFilterSortTodoListCategoryEndpoint</c> <i>produces</i>
/// <see cref="TodoListCategoryResponse.UncategorizedId"/> as a row the frontend can select;
/// <c>FilterSortTodoListEndpoint</c> <i>consumes</i> the same value out of
/// <c>TodoListFilterRequest.CategoryId</c> and reads it back as <c>CategoryId is null</c>. They share a
/// constant today, but nothing forces the producer and the consumer to keep agreeing on what it means:
/// change one side to <c>0</c>, or to "a real category id", and both endpoints still compile and still
/// answer 200 — the category picker just quietly stops filtering.
/// </summary>
[Collection("Postgres")]
public class SentinelRoundTripTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    [Fact(DisplayName = "The 'Other' id the category route hands out selects exactly the uncategorised lists on the list route")]
    public async Task OtherRowId_RoundTripsThroughTheListFilter()
    {
        long choresId;
        await using (var db = CreateDbContext())
        {
            choresId = await TodoListTestSeedHelper.SeedTodoListCategoryAsync(db, name: "Chores", ct: CancellationToken);
            await TodoListTestSeedHelper.SeedTodoListAsync(db, name: "Dishes", categoryId: choresId, ct: CancellationToken);
            await TodoListTestSeedHelper.SeedTodoListAsync(db, name: "Loose list", ct: CancellationToken);
        }

        var categories = await PostAsync<TodoListCategoryResponse>("api/todo-list-category/filter-sort", new
        {
            SortBy = Array.Empty<object>(),
            UseFilter = false,
            Filter = (object?)null
        });

        var otherId = categories.Should().ContainSingle(c => c.Name == TodoListCategoryResponse.UncategorizedName)
            .Subject.Id;

        // Taken from the response rather than from the constant on purpose: what the round trip has to
        // prove is that the value the category route actually served is the value the list route honours.
        var uncategorised = await ListsByCategoryAsync(otherId);
        uncategorised.Select(l => l.Name).Should().Equal("Loose list");

        var categorised = await ListsByCategoryAsync(choresId);
        categorised.Select(l => l.Name).Should().Equal("Dishes");
    }

    private Task<List<TodoListResponse>> ListsByCategoryAsync(long categoryId) =>
        PostAsync<TodoListResponse>("api/todo-list/filter-sort", new
        {
            SortBy = Array.Empty<object>(),
            UseFilter = true,
            Filter = new { Name = (string?)null, CategoryId = (long?)categoryId }
        });

    private async Task<List<T>> PostAsync<T>(string route, object payload)
    {
        var response = await CreateClient().PostAsJsonAsync(route, payload, JsonOpts, CancellationToken);

        var body = await response.Content.ReadAsStringAsync(CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);

        return JsonSerializer.Deserialize<List<T>>(body, JsonOpts)!;
    }
}
