using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using AdhdTimeOrganizer.IntegrationTests.Reminders;
using AdhdTimeOrganizer.TodoLists.application.dto.response.todoList;
using AdhdTimeOrganizer.TodoLists.domain.model.entity.todoList;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.application.dto.response.@base;
using Sydowwe.Framework.application.dto.response.generic;
using Sydowwe.Framework.Testing;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Endpoints;

/// <summary>
/// TEST-7 (+ TEST-11) — the TodoLists read routes that had no behavioural test at all, and the unique
/// <c>(UserId, Name)</c> index neither CRUD matrix reaches.
/// <para>
/// Every route here fails <b>silently</b>. A filter arm that stops matching answers 200 with the wrong
/// set; a projection that drops <c>Category</c> or miscounts <c>ItemCount</c> answers 200 with the wrong
/// numbers; and none of the four endpoints scopes by hand — all of them lean entirely on
/// <c>AppDbContext</c>'s global <c>IEntityWithUser</c> query filter, so the day that filter stops being
/// applied to one of these entities the route starts serving every user's rows with a 200 and no log
/// line. The class comments below name the specific mechanism each group is standing under.
/// </para>
/// <para>
/// <b>Rank band.</b> <c>task_priority</c> is unique on <c>(UserId, Priority)</c> and the whole
/// collection shares one test user, so every file has to claim a band. This file owns
/// <b>380–389</b> (<c>TaskPriorityReorderTests</c> uses 1–2, <c>TodoListDeleteBehaviorTests</c> 700+,
/// <c>TodoListCrudAuthMatrixTests</c> 20–106).
/// </para>
/// </summary>
/// <remarks>
/// <c>useFilter: true</c> is load-bearing on every filter request below: both
/// <c>BaseFilterSortEndpoint</c> and <c>BaseGridEndpoint</c> skip <c>ApplyCustomFiltering</c> entirely
/// when it is false, so a filter sent with <c>useFilter: false</c> applies nothing and every assertion
/// would pass vacuously.
/// </remarks>
[Collection("Postgres")]
public class FilterSortTodoListTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    [Fact(DisplayName = "The Name filter matches by substring, case-insensitively -- it runs as ILIKE, not LIKE")]
    public async Task NameFilter_MatchesSubstring_CaseInsensitively()
    {
        await using (var db = CreateDbContext())
        {
            await TodoListTestSeedHelper.SeedTodoListAsync(db, name: "Work", ct: CancellationToken);
            await TodoListTestSeedHelper.SeedTodoListAsync(db, name: "Workout", ct: CancellationToken);
            await TodoListTestSeedHelper.SeedTodoListAsync(db, name: "Home", ct: CancellationToken);
        }

        var matches = await FilterAsync(new { Name = "Work" });
        matches.Select(l => l.Name).Should().BeEquivalentTo(["Work", "Workout"]);

        // Rewritten deliberately, as the previous version of this comment asked: the filter now runs as
        // EF.Functions.ILike over FreeTextFilterExtensions.ToContainsPattern, in line with the grids.
        var lowercase = await FilterAsync(new { Name = "work" });
        lowercase.Select(l => l.Name).Should().BeEquivalentTo(["Work", "Workout"], "the filter is ILIKE, so case does not narrow it");

        // The escape has to survive the switch too — an unescaped % would turn this into a wildcard and
        // match every list, which is the failure mode FreeTextFilterExtensions' XML doc warns about.
        var wildcard = await FilterAsync(new { Name = "%" });
        wildcard.Should().BeEmpty("% is escaped to a literal, so it matches no list name");
    }

    /// <summary>
    /// The consumer half of the <c>-1</c> sentinel <c>GetFilterSortTodoListCategoryEndpoint</c> appends
    /// as the synthetic "Other" category. The round-trip belongs to that endpoint's own tests; this pins
    /// the arm that has to answer it.
    /// </summary>
    [Fact(DisplayName = "CategoryId = -1 returns only the uncategorised lists")]
    public async Task CategoryIdMinusOne_ReturnsUncategorisedOnly()
    {
        await using (var db = CreateDbContext())
        {
            var categoryId = await TodoListTestSeedHelper.SeedTodoListCategoryAsync(db, name: "Filed", ct: CancellationToken);
            await TodoListTestSeedHelper.SeedTodoListAsync(db, name: "Filed list", categoryId: categoryId, ct: CancellationToken);
            await TodoListTestSeedHelper.SeedTodoListAsync(db, name: "Loose list", ct: CancellationToken);
        }

        var matches = await FilterAsync(new { CategoryId = -1L });

        matches.Select(l => l.Name).Should().BeEquivalentTo(["Loose list"]);
    }

    [Fact(DisplayName = "CategoryId = a real id returns that category's lists only")]
    public async Task CategoryId_ReturnsThatCategoryOnly()
    {
        long wantedCategoryId;
        await using (var db = CreateDbContext())
        {
            wantedCategoryId = await TodoListTestSeedHelper.SeedTodoListCategoryAsync(db, name: "Wanted", ct: CancellationToken);
            var otherCategoryId = await TodoListTestSeedHelper.SeedTodoListCategoryAsync(db, name: "Unwanted", ct: CancellationToken);
            await TodoListTestSeedHelper.SeedTodoListAsync(db, name: "Wanted list", categoryId: wantedCategoryId, ct: CancellationToken);
            await TodoListTestSeedHelper.SeedTodoListAsync(db, name: "Unwanted list", categoryId: otherCategoryId, ct: CancellationToken);
            await TodoListTestSeedHelper.SeedTodoListAsync(db, name: "Loose list", ct: CancellationToken);
        }

        var matches = await FilterAsync(new { CategoryId = wantedCategoryId });

        matches.Select(l => l.Name).Should().BeEquivalentTo(["Wanted list"]);
    }

    [Fact(DisplayName = "CategoryId = null returns every list, categorised or not")]
    public async Task CategoryIdNull_ReturnsEverything()
    {
        await using (var db = CreateDbContext())
        {
            var categoryId = await TodoListTestSeedHelper.SeedTodoListCategoryAsync(db, name: "Filed", ct: CancellationToken);
            await TodoListTestSeedHelper.SeedTodoListAsync(db, name: "Filed list", categoryId: categoryId, ct: CancellationToken);
            await TodoListTestSeedHelper.SeedTodoListAsync(db, name: "Loose list", ct: CancellationToken);
        }

        var matches = await FilterAsync(new { Name = (string?)null, CategoryId = (long?)null });

        matches.Select(l => l.Name).Should().BeEquivalentTo(["Filed list", "Loose list"]);
    }

    /// <summary>
    /// <c>ItemCount</c> / <c>CompletedCount</c> are two correlated subqueries in
    /// <c>TodoListResponse.Projection</c>. PERF-3 proposes collapsing them into one aggregate; this is the
    /// test that has to stay green through that rewrite, which is why it asserts both a populated list and
    /// an empty one — an aggregate join is exactly the shape that drops the zero row.
    /// </summary>
    [Fact(DisplayName = "ItemCount and CompletedCount are computed per list, including 0/0 for an empty one")]
    public async Task ItemAndCompletedCounts_AreComputedPerList()
    {
        await using (var db = CreateDbContext())
        {
            var priorityId = await TodoListTestSeedHelper.SeedTaskPriorityAsync(db, 380, text: "counts", ct: CancellationToken);
            var listId = await TodoListTestSeedHelper.SeedTodoListAsync(db, name: "Counted list", ct: CancellationToken);

            // One activity per item: todo_list_item is unique on (UserId, ActivityId, TodoListId).
            foreach (var (suffix, isDone) in new[] { ("a", true), ("b", true), ("c", false) })
            {
                var activityId = await PlanningTestSeedHelper.SeedActivityAsync(db, $"counted-{suffix}", ct: CancellationToken);
                await TodoListTestSeedHelper.SeedTodoListItemAsync(db, activityId, priorityId, listId, isDone: isDone, ct: CancellationToken);
            }

            await TodoListTestSeedHelper.SeedTodoListAsync(db, name: "Empty list", ct: CancellationToken);
        }

        var lists = await FilterAsync(new { Name = (string?)null, CategoryId = (long?)null });

        var counted = lists.Single(l => l.Name == "Counted list");
        counted.ItemCount.Should().Be(3);
        counted.CompletedCount.Should().Be(2);

        var empty = lists.Single(l => l.Name == "Empty list");
        empty.ItemCount.Should().Be(0);
        empty.CompletedCount.Should().Be(0);
    }

    [Fact(DisplayName = "Category projects all four fields, and an uncategorised list projects a null Category")]
    public async Task CategoryProjection_AndItsNullArm()
    {
        await using (var db = CreateDbContext())
        {
            var category = new TodoListCategory
            {
                UserId = TodoListTestSeedHelper.TestUserId,
                Name = "Chores",
                Text = "Around the house",
                Color = "#123456",
                Icon = "fas fa-broom"
            };
            db.Set<TodoListCategory>().Add(category);
            await db.SaveChangesAsync(CancellationToken);

            await TodoListTestSeedHelper.SeedTodoListAsync(db, name: "Filed list", categoryId: category.Id, ct: CancellationToken);
            await TodoListTestSeedHelper.SeedTodoListAsync(db, name: "Loose list", ct: CancellationToken);
        }

        var lists = await FilterAsync(new { Name = (string?)null, CategoryId = (long?)null });

        var filed = lists.Single(l => l.Name == "Filed list");
        filed.Category.Should().NotBeNull();
        filed.Category!.Name.Should().Be("Chores");
        filed.Category.Text.Should().Be("Around the house");
        filed.Category.Color.Should().Be("#123456");
        filed.Category.Icon.Should().Be("fas fa-broom");

        lists.Single(l => l.Name == "Loose list").Category
            .Should().BeNull("the projection's explicit null guard is the only thing standing between an uncategorised list and a null-object category");
    }

    /// <summary>
    /// <c>FilterSortTodoListEndpoint</c> does not override <c>ApplyUserScoping</c>, so the global query
    /// filter is the whole of its user scoping. Nothing else would notice it going away.
    /// </summary>
    [Fact(DisplayName = "Another user's lists are not returned")]
    public async Task DoesNotLeakAnotherUsersLists()
    {
        await using (var db = CreateDbContext())
        {
            await ReminderSeedHelper.EnsureOtherUserAsync(db, CancellationToken);
            await TodoListTestSeedHelper.SeedTodoListAsync(db, name: "Mine", ct: CancellationToken);
            await TodoListTestSeedHelper.SeedTodoListAsync(db, ReminderSeedHelper.OtherUserId, "Theirs", ct: CancellationToken);
        }

        var matches = await FilterAsync(new { Name = (string?)null, CategoryId = (long?)null });

        matches.Select(l => l.Name).Should().BeEquivalentTo(["Mine"]);
    }

    [Fact(DisplayName = "Unauthenticated filter-sort is 401")]
    public async Task Unauthenticated_Returns401()
    {
        var response = await CreateUnauthenticatedClient().PostAsJsonAsync("api/todo-list/filter-sort", new
        {
            SortBy = Array.Empty<object>(),
            UseFilter = false,
            Filter = (object?)null
        }, JsonOpts, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private async Task<List<TodoListResponse>> FilterAsync(object filter)
    {
        var response = await CreateClient().PostAsJsonAsync("api/todo-list/filter-sort", new
        {
            SortBy = Array.Empty<object>(),
            UseFilter = true,
            Filter = filter
        }, JsonOpts, CancellationToken);

        var body = await response.Content.ReadAsStringAsync(CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);

        return JsonSerializer.Deserialize<List<TodoListResponse>>(body, JsonOpts)!;
    }
}

/// <summary>
/// TEST-7 / Section B — <c>GridTaskPriorityEndpoint</c>'s four filter arms, its paging envelope and its
/// user scoping. The arms are not uniform (<c>Text</c> is <c>Contains</c>, <c>Color</c> is <c>==</c>) and
/// nothing recorded that, so "make the filters consistent" is a plausible future change that would break
/// callers with no failing build behind it. Each arm is pinned as it actually behaves.
/// </summary>
[Collection("Postgres")]
public class GridTaskPriorityTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    // Rank band 380-389, see FilterSortTodoListTests.
    private const int SprintRank = 381;
    private const int SteadyRank = 382;
    private const int GammaRank = 383;
    private const int DeltaRank = 384;

    /// <summary>
    /// Seeded by hand rather than through <c>TodoListTestSeedHelper</c> because the colours matter here:
    /// the helper gives every row the same one, and two rows sharing a colour is what makes the
    /// exact-match arm distinguishable from a substring one.
    /// </summary>
    protected override async Task SeedAsync(DbContext db)
    {
        db.Set<TaskPriority>().AddRange(
            new TaskPriority { UserId = TodoListTestSeedHelper.TestUserId, Text = "Sprint Rush", Color = "#111111", Priority = SprintRank },
            new TaskPriority { UserId = TodoListTestSeedHelper.TestUserId, Text = "Steady Rush", Color = "#222222", Priority = SteadyRank },
            new TaskPriority { UserId = TodoListTestSeedHelper.TestUserId, Text = "Gamma Calm", Color = "#111111", Priority = GammaRank },
            new TaskPriority { UserId = TodoListTestSeedHelper.TestUserId, Text = "Delta Calm", Color = "#444444", Priority = DeltaRank });

        await db.SaveChangesAsync(CancellationToken);
    }

    [Fact(DisplayName = "The Text filter matches by substring, case-insensitively -- it runs as ILIKE, not LIKE")]
    public async Task TextFilter_MatchesSubstring_CaseInsensitively()
    {
        var matches = await GridAsync(new { Text = "Rush" });
        matches.Items.Select(p => p.Priority).Should().Equal(SprintRank, SteadyRank);

        var lowercase = await GridAsync(new { Text = "rush" });
        lowercase.Items.Select(p => p.Priority).Should().Equal(SprintRank, SteadyRank);

        var wildcard = await GridAsync(new { Text = "%" });
        wildcard.Items.Should().BeEmpty("% is escaped to a literal, so it matches no priority text");
    }

    /// <summary>
    /// <c>Color</c> is an equality arm, unlike <c>Text</c> next to it. The partial value is the assertion
    /// that tells the two apart — a count-only check against the exact value would pass either way.
    /// </summary>
    [Fact(DisplayName = "The Color filter is an exact match, not a substring one")]
    public async Task ColorFilter_IsExactMatch()
    {
        var exact = await GridAsync(new { Color = "#111111" });
        exact.Items.Select(p => p.Priority).Should().Equal(SprintRank, GammaRank);

        var partial = await GridAsync(new { Color = "111111" });
        partial.Items.Should().BeEmpty("Color compares with ==, so a substring of a real colour matches nothing");
    }

    [Fact(DisplayName = "MinPriority alone is an inclusive lower bound")]
    public async Task MinPriority_IsInclusiveLowerBound()
    {
        var matches = await GridAsync(new { MinPriority = GammaRank });

        matches.Items.Select(p => p.Priority).Should().Equal(GammaRank, DeltaRank);
    }

    [Fact(DisplayName = "MaxPriority alone is an inclusive upper bound")]
    public async Task MaxPriority_IsInclusiveUpperBound()
    {
        var matches = await GridAsync(new { MaxPriority = SteadyRank });

        matches.Items.Select(p => p.Priority).Should().Equal(SprintRank, SteadyRank);
    }

    [Fact(DisplayName = "Min and Max together are a closed range, and an inverted range is empty rather than an error")]
    public async Task MinAndMax_AreAClosedRange()
    {
        var closed = await GridAsync(new { MinPriority = SteadyRank, MaxPriority = GammaRank });
        closed.Items.Select(p => p.Priority).Should().Equal(SteadyRank, GammaRank);

        var inverted = await GridAsync(new { MinPriority = DeltaRank, MaxPriority = SprintRank });
        inverted.Items.Should().BeEmpty();
        inverted.ItemsCount.Should().Be(0, "an impossible range is an empty page, not a 400");
    }

    [Fact(DisplayName = "A filter with every arm null returns everything")]
    public async Task AllArmsNull_ReturnsEverything()
    {
        var matches = await GridAsync(new
        {
            Text = (string?)null,
            Color = (string?)null,
            MinPriority = (int?)null,
            MaxPriority = (int?)null
        });

        matches.Items.Select(p => p.Priority).Should().Equal(SprintRank, SteadyRank, GammaRank, DeltaRank);
    }

    [Fact(DisplayName = "Paging returns the right slice, and ItemsCount / PageCount describe the whole set")]
    public async Task Paging_SlicesAndCountsCorrectly()
    {
        var firstPage = await GridAsync(filter: null, itemsPerPage: 2, page: 1);
        firstPage.Items.Select(p => p.Priority).Should().Equal(SprintRank, SteadyRank);
        firstPage.ItemsCount.Should().Be(4, "ItemsCount counts the filtered set, not the page");
        firstPage.PageCount.Should().Be(2);

        var secondPage = await GridAsync(filter: null, itemsPerPage: 2, page: 2);
        secondPage.Items.Select(p => p.Priority).Should().Equal(GammaRank, DeltaRank);
        secondPage.ItemsCount.Should().Be(4);
        secondPage.PageCount.Should().Be(2);
    }

    [Fact(DisplayName = "Another user's priorities are not returned")]
    public async Task DoesNotLeakAnotherUsersPriorities()
    {
        await using (var db = CreateDbContext())
        {
            await ReminderSeedHelper.EnsureOtherUserAsync(db, CancellationToken);
            await TodoListTestSeedHelper.SeedTaskPriorityAsync(db, 385, ReminderSeedHelper.OtherUserId, "Theirs", CancellationToken);
        }

        var matches = await GridAsync(filter: null);

        matches.Items.Should().HaveCount(4);
        matches.Items.Should().NotContain(p => p.Text == "Theirs");
        matches.ItemsCount.Should().Be(4, "the count runs before the projection, so an unscoped query shows up here first");
    }

    [Fact(DisplayName = "Unauthenticated grid is 401")]
    public async Task Unauthenticated_Returns401()
    {
        var response = await CreateUnauthenticatedClient().PostAsJsonAsync("api/task-priority/filtered-table", new
        {
            Page = 1,
            ItemsPerPage = 100,
            SortBy = Array.Empty<object>(),
            UseFilter = false,
            Filter = (object?)null
        }, JsonOpts, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private async Task<BaseGridResponse<TaskPriorityResponse>> GridAsync(object? filter, int itemsPerPage = 100, int page = 1)
    {
        var response = await CreateClient().PostAsJsonAsync("api/task-priority/filtered-table", new
        {
            Page = page,
            ItemsPerPage = itemsPerPage,
            // Ascending rank, so every assertion above can use an ordered Equal rather than a set comparison.
            SortBy = new[] { new { Key = "priority", IsDesc = false } },
            UseFilter = filter is not null,
            Filter = filter
        }, JsonOpts, CancellationToken);

        var body = await response.Content.ReadAsStringAsync(CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);

        return JsonSerializer.Deserialize<BaseGridResponse<TaskPriorityResponse>>(body, JsonOpts)!;
    }
}

/// <summary>
/// TEST-7 / Section C — the three <c>all-options</c> routes. They look interchangeable and are not: two
/// build <c>SelectOptionResponse</c> through its positional constructor and one through object-initialiser
/// syntax, so a swapped <c>(id, text)</c> pair is a compile-time no-op that only a test reading both
/// members catches. One of the three overrides <c>Sort</c>; the other two inherit the base's order by id.
/// </summary>
[Collection("Postgres")]
public class TodoListSelectOptionsTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    [Fact(DisplayName = "GET /todo-list/all-options pairs each list's id with its Name")]
    public async Task TodoListOptions_PairIdWithName()
    {
        long listId;
        await using (var db = CreateDbContext())
            listId = await TodoListTestSeedHelper.SeedTodoListAsync(db, name: "Groceries", ct: CancellationToken);

        var options = await OptionsAsync("api/todo-list/all-options");

        options.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new { Id = listId, Text = "Groceries" });
    }

    [Fact(DisplayName = "GET /todo-list-category/all-options pairs each category's id with its Name")]
    public async Task CategoryOptions_PairIdWithName()
    {
        long categoryId;
        await using (var db = CreateDbContext())
            categoryId = await TodoListTestSeedHelper.SeedTodoListCategoryAsync(db, name: "Chores", ct: CancellationToken);

        var options = await OptionsAsync("api/todo-list-category/all-options");

        options.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new { Id = categoryId, Text = "Chores" });
    }

    [Fact(DisplayName = "GET /task-priority/all-options pairs each priority's id with its Text")]
    public async Task TaskPriorityOptions_PairIdWithText()
    {
        long priorityId;
        await using (var db = CreateDbContext())
            priorityId = await TodoListTestSeedHelper.SeedTaskPriorityAsync(db, 381, text: "Urgent", ct: CancellationToken);

        var options = await OptionsAsync("api/task-priority/all-options");

        options.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new { Id = priorityId, Text = "Urgent" });
    }

    /// <summary>
    /// <c>GetSelectOptionsTodoListCategoryEndpoint</c> is the only one of the three that overrides
    /// <c>Sort</c>, which makes it the only one that can regress to insertion order without anything else
    /// changing. Seeded deliberately out of alphabetical order so id order and name order disagree.
    /// </summary>
    [Fact(DisplayName = "Category options are ordered by Name, not by id")]
    public async Task CategoryOptions_AreOrderedByName()
    {
        await using (var db = CreateDbContext())
        {
            await TodoListTestSeedHelper.SeedTodoListCategoryAsync(db, name: "Zebra", ct: CancellationToken);
            await TodoListTestSeedHelper.SeedTodoListCategoryAsync(db, name: "Alpha", ct: CancellationToken);
            await TodoListTestSeedHelper.SeedTodoListCategoryAsync(db, name: "Mango", ct: CancellationToken);
        }

        var options = await OptionsAsync("api/todo-list-category/all-options");

        options.Select(o => o.Text).Should().Equal("Alpha", "Mango", "Zebra");
    }

    /// <summary>
    /// The observed behaviour, pinned rather than endorsed: <c>GetSelectOptionsTaskPriorityEndpoint</c>
    /// overrides only <c>Map</c>, so ordering falls back to <c>BaseGetSelectOptionsEndpoint</c>'s order by
    /// id — i.e. the order the rows happened to be created in. A priority picker listing ranks out of
    /// order is a plausible product bug; this test is what makes the question visible. If the answer is
    /// "it should sort by Priority", change the endpoint and rewrite this test — do not let the order
    /// drift silently.
    /// </summary>
    [Fact(DisplayName = "Task-priority options come back in id order, NOT ranked by Priority")]
    public async Task TaskPriorityOptions_AreNotOrderedByPriority()
    {
        await using (var db = CreateDbContext())
        {
            await TodoListTestSeedHelper.SeedTaskPriorityAsync(db, 384, text: "Created first", ct: CancellationToken);
            await TodoListTestSeedHelper.SeedTaskPriorityAsync(db, 381, text: "Created second", ct: CancellationToken);
            await TodoListTestSeedHelper.SeedTaskPriorityAsync(db, 383, text: "Created third", ct: CancellationToken);
        }

        var options = await OptionsAsync("api/task-priority/all-options");

        options.Select(o => o.Text).Should().Equal("Created first", "Created second", "Created third");
    }

    [Fact(DisplayName = "None of the three all-options routes returns another user's rows")]
    public async Task AllThreeRoutes_AreUserScoped()
    {
        await using (var db = CreateDbContext())
        {
            await ReminderSeedHelper.EnsureOtherUserAsync(db, CancellationToken);

            await TodoListTestSeedHelper.SeedTodoListAsync(db, name: "My list", ct: CancellationToken);
            await TodoListTestSeedHelper.SeedTodoListCategoryAsync(db, name: "My category", ct: CancellationToken);
            await TodoListTestSeedHelper.SeedTaskPriorityAsync(db, 386, text: "My priority", ct: CancellationToken);

            await TodoListTestSeedHelper.SeedTodoListAsync(db, ReminderSeedHelper.OtherUserId, "Their list", ct: CancellationToken);
            await TodoListTestSeedHelper.SeedTodoListCategoryAsync(db, ReminderSeedHelper.OtherUserId, "Their category", CancellationToken);
            await TodoListTestSeedHelper.SeedTaskPriorityAsync(db, 387, ReminderSeedHelper.OtherUserId, "Their priority", CancellationToken);
        }

        (await OptionsAsync("api/todo-list/all-options")).Select(o => o.Text).Should().Equal("My list");
        (await OptionsAsync("api/todo-list-category/all-options")).Select(o => o.Text).Should().Equal("My category");
        (await OptionsAsync("api/task-priority/all-options")).Select(o => o.Text).Should().Equal("My priority");
    }

    [Fact(DisplayName = "Unauthenticated all-options is 401 (representative of all three)")]
    public async Task Unauthenticated_Returns401()
    {
        var response = await CreateUnauthenticatedClient().GetAsync("api/todo-list/all-options", CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private async Task<List<SelectOptionResponse>> OptionsAsync(string route)
    {
        var response = await CreateClient().GetAsync(route, CancellationToken);

        var body = await response.Content.ReadAsStringAsync(CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);

        return JsonSerializer.Deserialize<List<SelectOptionResponse>>(body, JsonOpts)!;
    }
}

/// <summary>
/// TEST-7 / Section D — <c>GET /api/task-priority</c> had only a <c>NotBe(404)</c> smoke test, which a
/// route returning every user's rows, or none of the projected fields, would pass unchanged.
/// </summary>
[Collection("Postgres")]
public class GetAllTaskPriorityTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    [Fact(DisplayName = "GET /task-priority projects Id/Text/Color/Priority for the caller's rows only")]
    public async Task ReturnsTheCallersPriorities_FullyProjected()
    {
        long urgentId;
        await using (var db = CreateDbContext())
        {
            await ReminderSeedHelper.EnsureOtherUserAsync(db, CancellationToken);

            var urgent = new TaskPriority
            {
                UserId = TodoListTestSeedHelper.TestUserId,
                Text = "Urgent",
                Color = "#ff0000",
                Priority = 388
            };
            db.Set<TaskPriority>().Add(urgent);
            await db.SaveChangesAsync(CancellationToken);
            urgentId = urgent.Id;

            await TodoListTestSeedHelper.SeedTaskPriorityAsync(db, 389, ReminderSeedHelper.OtherUserId, "Theirs", CancellationToken);
        }

        var response = await CreateClient().GetAsync("api/task-priority", CancellationToken);
        var body = await response.Content.ReadAsStringAsync(CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);

        var priorities = JsonSerializer.Deserialize<List<TaskPriorityResponse>>(body, JsonOpts)!;

        priorities.Should().ContainSingle("the other user's row must not be visible")
            .Which.Should().BeEquivalentTo(new
            {
                Id = urgentId,
                Text = "Urgent",
                Color = "#ff0000",
                Priority = 388
            });
    }
}

/// <summary>
/// TEST-11 — <c>TodoList</c> and <c>TodoListCategory</c> both declare
/// <c>HasIndex(r =&gt; new { r.UserId, r.Name }).IsUnique()</c> and nothing tested either. The index is
/// only visible through the status code a collision produces: <c>DbUtils.HandleException</c> maps
/// Postgres 23505 to a 409, and losing the index turns that into a silently duplicated row, while losing
/// the mapping turns it into a 500. <c>TaskPriorityReorderTests</c> set the expectation this follows —
/// a clean 409, and nothing partially applied.
/// </summary>
[Collection("Postgres")]
public class TodoListUniqueNameTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    [Fact(DisplayName = "Creating a second TodoList with an existing name is a clean 409, and leaves one row")]
    public async Task CreateTodoList_DuplicateName_Returns409()
    {
        var client = CreateClient();

        var first = await client.PostAsJsonAsync("api/todo-list", new { Name = "Work" }, JsonOpts, CancellationToken);
        first.StatusCode.Should().Be(HttpStatusCode.Created);

        var second = await client.PostAsJsonAsync("api/todo-list", new { Name = "Work" }, JsonOpts, CancellationToken);
        second.StatusCode.Should().Be(HttpStatusCode.Conflict);

        await using var verifyDb = CreateDbContext();
        (await verifyDb.Set<TodoList>().IgnoreQueryFilters().CountAsync(l => l.Name == "Work", CancellationToken))
            .Should().Be(1);
    }

    [Fact(DisplayName = "Renaming a TodoList onto a sibling's name is a clean 409, and leaves both rows unchanged")]
    public async Task UpdateTodoList_OntoSiblingName_Returns409()
    {
        long renamedId, siblingId;
        await using (var db = CreateDbContext())
        {
            renamedId = await TodoListTestSeedHelper.SeedTodoListAsync(db, name: "Home", ct: CancellationToken);
            siblingId = await TodoListTestSeedHelper.SeedTodoListAsync(db, name: "Work", ct: CancellationToken);
        }

        var response = await CreateClient().PutAsJsonAsync($"api/todo-list/{renamedId}", new { Name = "Work" }, JsonOpts, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);

        await using var verifyDb = CreateDbContext();
        (await verifyDb.Set<TodoList>().IgnoreQueryFilters().SingleAsync(l => l.Id == renamedId, CancellationToken)).Name.Should().Be("Home");
        (await verifyDb.Set<TodoList>().IgnoreQueryFilters().SingleAsync(l => l.Id == siblingId, CancellationToken)).Name.Should().Be("Work");
    }

    [Fact(DisplayName = "Creating a second TodoListCategory with an existing name is a clean 409, and leaves one row")]
    public async Task CreateTodoListCategory_DuplicateName_Returns409()
    {
        var client = CreateClient();
        var payload = new { Name = "Chores", Color = "#ff00ff" };

        var first = await client.PostAsJsonAsync("api/todo-list-category", payload, JsonOpts, CancellationToken);
        first.StatusCode.Should().Be(HttpStatusCode.Created);

        var second = await client.PostAsJsonAsync("api/todo-list-category", payload, JsonOpts, CancellationToken);
        second.StatusCode.Should().Be(HttpStatusCode.Conflict);

        await using var verifyDb = CreateDbContext();
        (await verifyDb.Set<TodoListCategory>().IgnoreQueryFilters().CountAsync(c => c.Name == "Chores", CancellationToken))
            .Should().Be(1);
    }

    [Fact(DisplayName = "Renaming a TodoListCategory onto a sibling's name is a clean 409, and leaves both rows unchanged")]
    public async Task UpdateTodoListCategory_OntoSiblingName_Returns409()
    {
        long renamedId, siblingId;
        await using (var db = CreateDbContext())
        {
            renamedId = await TodoListTestSeedHelper.SeedTodoListCategoryAsync(db, name: "Errands", ct: CancellationToken);
            siblingId = await TodoListTestSeedHelper.SeedTodoListCategoryAsync(db, name: "Chores", ct: CancellationToken);
        }

        var response = await CreateClient().PutAsJsonAsync($"api/todo-list-category/{renamedId}",
            new { Name = "Chores", Color = "#00ff00" }, JsonOpts, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);

        await using var verifyDb = CreateDbContext();
        (await verifyDb.Set<TodoListCategory>().IgnoreQueryFilters().SingleAsync(c => c.Id == renamedId, CancellationToken)).Name.Should().Be("Errands");
        (await verifyDb.Set<TodoListCategory>().IgnoreQueryFilters().SingleAsync(c => c.Id == siblingId, CancellationToken)).Name.Should().Be("Chores");
    }

    /// <summary>
    /// The arm that breaks if the index is ever "simplified" to <c>Name</c> alone: it is
    /// <c>(UserId, Name)</c>, so two users may each own a list — and a category — called "Work".
    /// </summary>
    [Fact(DisplayName = "A name another user already owns does not collide -- the index is (UserId, Name)")]
    public async Task SameNameOwnedByAnotherUser_DoesNotCollide()
    {
        await using (var db = CreateDbContext())
        {
            await ReminderSeedHelper.EnsureOtherUserAsync(db, CancellationToken);
            await TodoListTestSeedHelper.SeedTodoListAsync(db, ReminderSeedHelper.OtherUserId, "Work", ct: CancellationToken);

            var theirCategory = new TodoListCategory
            {
                UserId = ReminderSeedHelper.OtherUserId,
                Name = "Chores",
                Color = "#abcdef"
            };
            db.Set<TodoListCategory>().Add(theirCategory);
            await db.SaveChangesAsync(CancellationToken);
        }

        var client = CreateClient();

        var list = await client.PostAsJsonAsync("api/todo-list", new { Name = "Work" }, JsonOpts, CancellationToken);
        list.StatusCode.Should().Be(HttpStatusCode.Created);

        var category = await client.PostAsJsonAsync("api/todo-list-category",
            new { Name = "Chores", Color = "#ff00ff" }, JsonOpts, CancellationToken);
        category.StatusCode.Should().Be(HttpStatusCode.Created);

        await using var verifyDb = CreateDbContext();
        (await verifyDb.Set<TodoList>().IgnoreQueryFilters().CountAsync(l => l.Name == "Work", CancellationToken)).Should().Be(2);
        (await verifyDb.Set<TodoListCategory>().IgnoreQueryFilters().CountAsync(c => c.Name == "Chores", CancellationToken)).Should().Be(2);
    }
}
