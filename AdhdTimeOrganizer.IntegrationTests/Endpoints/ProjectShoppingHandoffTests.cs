using System.Net;
using System.Net.Http.Json;
using AdhdTimeOrganizer.ActivityProfiles.domain.model.entity;
using AdhdTimeOrganizer.ActivityProfiles.domain.model.@enum;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using AdhdTimeOrganizer.TodoLists.domain.model.entity.todoList;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.Testing;
using Xunit;
using static AdhdTimeOrganizer.IntegrationTests.Endpoints.ActivityProfileTestSeedHelper;

namespace AdhdTimeOrganizer.IntegrationTests.Endpoints;

/// <summary>
/// FO13 — a project becomes suggestible again once its shopping is done. The handoff links its to-do item
/// with <c>PATCH /activity-project-profile/{id}/shopping-item</c>; ticking that item done moves a
/// <c>NeedsShopping</c> project to <c>ReadyToStart</c> through <c>ProjectShoppingDoneEventHandler</c>.
/// </summary>
/// <remarks>
/// The handler is a seam-event subscriber, so it logs and swallows: a broken join, a dropped assembly or a
/// handler that never fires all leave the toggle answering 204. Every case therefore asserts on the stored
/// <c>ReadinessStatus</c>, never on a status code alone. The precision case matters most: the handoff item
/// shares the project's activity, so a handler that matched on the activity would look right in every other
/// test here and still fire on any to-do for the same project.
/// </remarks>
[Collection("Postgres")]
public class ProjectShoppingHandoffTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    private const long UserId = FakeLoggedUserService.TestUserId;

    private long _taskPriorityId;

    protected override async Task SeedAsync(DbContext db)
    {
        var priority = new TaskPriority { UserId = UserId, Text = "Normal", Color = "#556677", Priority = 1 };
        db.Set<TaskPriority>().Add(priority);
        await db.SaveChangesAsync(CancellationToken);
        _taskPriorityId = priority.Id;
    }

    [Fact(DisplayName = "Ticking the linked shopping item moves a NeedsShopping project to ReadyToStart, and un-ticking leaves it there")]
    public async Task TickingTheShoppingItem_MakesTheProjectReady()
    {
        var (profileId, itemId) = await SeedLinkedAsync("Bookshelf", ReadinessStatus.NeedsShopping);

        (await ToggleItemAsync(itemId, true)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await StatusOfAsync(profileId)).Should().Be(ReadinessStatus.ReadyToStart);

        (await ToggleItemAsync(itemId, false)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await StatusOfAsync(profileId)).Should().Be(ReadinessStatus.ReadyToStart, "the materials are bought; un-ticking does not un-buy them");
    }

    [Fact(DisplayName = "Ticking the last step completes the item, and that moves the project too")]
    public async Task TickingTheLastStep_MakesTheProjectReady()
    {
        var (profileId, itemId) = await SeedLinkedAsync("Birdhouse", ReadinessStatus.NeedsShopping, stepCount: 1);
        Guid stepId;
        await using (var db = CreateDbContext())
            stepId = (await db.Set<TodoListItem>().Include(i => i.Steps).SingleAsync(i => i.Id == itemId, CancellationToken)).Steps.Single().Id;

        var toggle = await CreateClient().PatchAsync($"api/todo-list-item/{itemId}/steps/{stepId}/toggle",
            new StringContent(string.Empty), CancellationToken);
        toggle.StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await StatusOfAsync(profileId)).Should().Be(ReadinessStatus.ReadyToStart);
    }

    [Fact(DisplayName = "A project still in Planning is not skipped ahead by its shopping item")]
    public async Task APlanningProject_IsLeftAlone()
    {
        var (profileId, itemId) = await SeedLinkedAsync("Garden bench", ReadinessStatus.Planning);

        (await ToggleItemAsync(itemId, true)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await StatusOfAsync(profileId)).Should().Be(ReadinessStatus.Planning);
    }

    [Fact(DisplayName = "An unlinked to-do for the same activity does not move the project — the link decides, not the activity")]
    public async Task AnUnlinkedItemForTheSameActivity_DoesNotMoveTheProject()
    {
        var (profileId, linkedItemId) = await SeedLinkedAsync("Coffee table", ReadinessStatus.NeedsShopping);
        long otherItemId;
        await using (var db = CreateDbContext())
        {
            var activityId = (await db.Set<ActivityProjectProfile>().SingleAsync(p => p.Id == profileId, CancellationToken)).ActivityId;
            otherItemId = await SeedItemAsync(db, activityId);
        }

        (await ToggleItemAsync(otherItemId, true)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await StatusOfAsync(profileId)).Should().Be(ReadinessStatus.NeedsShopping,
            "\"work on the coffee table\" shares the activity but is not the shopping trip");

        (await ToggleItemAsync(linkedItemId, true)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await StatusOfAsync(profileId)).Should().Be(ReadinessStatus.ReadyToStart);
    }

    [Fact(DisplayName = "Linking another account's to-do item is a 404 and writes nothing")]
    public async Task LinkingAForeignItem_Is404()
    {
        var userIdB = await CreateSecondUserAsync(Fixture, "shopping-b@test.com");
        long profileId, foreignItemId;
        await using (var db = CreateDbContext())
        {
            (_, profileId) = await SeedProjectProfileAsync(db, UserId, "Shelf");
            var foreignActivityId = await SeedActivityAsync(db, userIdB, "B's thing");
            var priorityB = new TaskPriority { UserId = userIdB, Text = "B", Color = "#000000", Priority = 1 };
            db.Set<TaskPriority>().Add(priorityB);
            await db.SaveChangesAsync(CancellationToken);
            var item = new TodoListItem { UserId = userIdB, ActivityId = foreignActivityId, TaskPriorityId = priorityB.Id };
            db.Set<TodoListItem>().Add(item);
            await db.SaveChangesAsync(CancellationToken);
            foreignItemId = item.Id;
        }

        var response = await LinkAsync(profileId, foreignItemId);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        await using var assertDb = CreateDbContext();
        (await assertDb.Set<ActivityProjectProfile>().AsNoTracking().SingleAsync(p => p.Id == profileId, CancellationToken))
            .ShoppingTodoListItemId.Should().BeNull();
    }

    [Fact(DisplayName = "Linking from another account onto my project is refused and writes nothing")]
    public async Task LinkingOntoAForeignProject_IsRefused()
    {
        var userIdB = await CreateSecondUserAsync(Fixture, "shopping-c@test.com");
        var (profileId, itemId) = await SeedLinkedAsync("Planter", ReadinessStatus.NeedsShopping, link: false);

        await using var factoryB = CreateFactory(["User"], userIdB);
        var response = await factoryB.CreateClient().PatchAsJsonAsync($"api/activity-project-profile/{profileId}/shopping-item",
            new { TodoListItemId = itemId }, JsonOpts, CancellationToken);

        response.IsSuccessStatusCode.Should().BeFalse();
        await using var assertDb = CreateDbContext();
        (await assertDb.Set<ActivityProjectProfile>().AsNoTracking().SingleAsync(p => p.Id == profileId, CancellationToken))
            .ShoppingTodoListItemId.Should().BeNull();
    }

    [Fact(DisplayName = "Saving the profile form keeps the link — the full update does not carry it")]
    public async Task UpdatingTheProfile_KeepsTheLink()
    {
        var (profileId, itemId) = await SeedLinkedAsync("Stool", ReadinessStatus.NeedsShopping);
        long activityId;
        await using (var db = CreateDbContext())
            activityId = (await db.Set<ActivityProjectProfile>().SingleAsync(p => p.Id == profileId, CancellationToken)).ActivityId;

        var update = await CreateClient().PutAsJsonAsync($"api/activity-project-profile/{profileId}", new
        {
            ActivityId = activityId,
            DifficultyLevel = "Beginner",
            ProjectArea = "Woodworking",
            EstimatedHours = 5,
            IsMessy = false,
            MaterialsNeeded = new[] { "Boards" },
            RequiredTools = Array.Empty<string>(),
            ReadinessStatus = "NeedsShopping"
        }, JsonOpts, CancellationToken);
        update.IsSuccessStatusCode.Should().BeTrue();

        await using var assertDb = CreateDbContext();
        (await assertDb.Set<ActivityProjectProfile>().AsNoTracking().SingleAsync(p => p.Id == profileId, CancellationToken))
            .ShoppingTodoListItemId.Should().Be(itemId);
    }

    [Fact(DisplayName = "Deleting the shopping item clears the link and keeps the project")]
    public async Task DeletingTheItem_ClearsTheLink()
    {
        var (profileId, itemId) = await SeedLinkedAsync("Lamp", ReadinessStatus.NeedsShopping);

        await using (var db = CreateDbContext())
        {
            await db.Set<TodoListItem>().Where(i => i.Id == itemId).ExecuteDeleteAsync(CancellationToken);
        }

        await using var assertDb = CreateDbContext();
        var profile = await assertDb.Set<ActivityProjectProfile>().AsNoTracking().SingleAsync(p => p.Id == profileId, CancellationToken);
        profile.ShoppingTodoListItemId.Should().BeNull();
        profile.ReadinessStatus.Should().Be(ReadinessStatus.NeedsShopping);
    }

    [Fact(DisplayName = "The profile read carries the link, so the board can say the trip is on a list")]
    public async Task TheProfileRead_CarriesTheLink()
    {
        var (profileId, itemId) = await SeedLinkedAsync("Mirror frame", ReadinessStatus.NeedsShopping);

        var body = await CreateClient().GetFromJsonAsync<Dictionary<string, object?>>($"api/activity-project-profile/{profileId}", JsonOpts, CancellationToken);

        body.Should().NotBeNull();
        body!["shoppingTodoListItemId"]!.ToString().Should().Be(itemId.ToString());
    }

    // ---- helpers ----------------------------------------------------------------------------------------

    /// <summary>A project in <paramref name="status"/> with a to-do item for its own activity, linked through the real route.</summary>
    private async Task<(long ProfileId, long ItemId)> SeedLinkedAsync(string name, ReadinessStatus status, int stepCount = 0, bool link = true)
    {
        long profileId, itemId;
        await using (var db = CreateDbContext())
        {
            long activityId;
            (activityId, profileId) = await SeedProjectProfileAsync(db, UserId, name);
            var profile = await db.Set<ActivityProjectProfile>().SingleAsync(p => p.Id == profileId, CancellationToken);
            profile.ReadinessStatus = status;
            await db.SaveChangesAsync(CancellationToken);
            itemId = await SeedItemAsync(db, activityId, stepCount);
        }

        if (link)
            (await LinkAsync(profileId, itemId)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        return (profileId, itemId);
    }

    private async Task<long> SeedItemAsync(DbContext db, long activityId, int stepCount = 0)
    {
        var item = new TodoListItem { UserId = UserId, ActivityId = activityId, TaskPriorityId = _taskPriorityId };
        for (var i = 0; i < stepCount; i++)
            item.Steps.Add(new TodoListStep { Name = $"step-{i}" });
        db.Set<TodoListItem>().Add(item);
        await db.SaveChangesAsync(CancellationToken);
        return item.Id;
    }

    private Task<HttpResponseMessage> LinkAsync(long profileId, long itemId) =>
        CreateClient().PatchAsJsonAsync($"api/activity-project-profile/{profileId}/shopping-item",
            new { TodoListItemId = itemId }, JsonOpts, CancellationToken);

    private Task<HttpResponseMessage> ToggleItemAsync(long itemId, bool forceValue) =>
        CreateClient().PatchAsJsonAsync("api/todo-list-item/toggle-is-done",
            new { Ids = new[] { itemId }, ForceValue = forceValue }, JsonOpts, CancellationToken);

    private async Task<ReadinessStatus> StatusOfAsync(long profileId)
    {
        await using var db = CreateDbContext();
        return (await db.Set<ActivityProjectProfile>().AsNoTracking().SingleAsync(p => p.Id == profileId, CancellationToken)).ReadinessStatus;
    }
}
