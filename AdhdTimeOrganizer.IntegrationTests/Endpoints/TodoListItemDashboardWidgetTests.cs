using System.Net;
using System.Text.Json;
using AdhdTimeOrganizer.Core.domain.model.entity.user;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using AdhdTimeOrganizer.IntegrationTests.Reminders;
using AdhdTimeOrganizer.TodoLists.application.dto.response.todoList;
using AdhdTimeOrganizer.TodoLists.domain.model.entity.todoList;
using AdhdTimeOrganizer.TodoLists.infrastructure.settings;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Sydowwe.Framework.Testing;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Endpoints;

/// <summary>
/// <c>TEST-5</c> / <c>CQ-18</c> — <c>GET /api/todo-list-item/dashboard-widget</c>, which had no test of any
/// kind, not even the route smoke test. Every one of its moving parts fails <b>silently</b>:
/// <list type="bullet">
/// <item>The window is two overrides on <c>BaseGetAllEndpoint</c>. A dropped <c>Sort</c> answers 200 with the
/// widget silently rearranged; a <c>&lt;</c> where the <c>&lt;=</c> is answers 200 a day short.</item>
/// <item>The horizon's end is the <b>user's</b> date, resolved through <c>IUserTimeZoneResolver</c>. It used to
/// be <c>DateTime.Today</c> — the server process's local date — which drops a task on its due date for a user
/// east of the server and shows one a day early for a user west of it. Nothing about that reads as an error:
/// the same request simply answers differently depending on where the host runs.</item>
/// <item>Nothing in the endpoint scopes rows to the caller. The global <c>IEntityWithUser</c> query filter does,
/// and a filtered query like this one is the shape most likely to be rewritten past it.</item>
/// </list>
///
/// <para>The horizon is read from <c>TodoListSettings.DashboardHorizonDays</c> out of the host rather than from
/// a literal 3, so a host that retunes the window retunes these cases with it;
/// <see cref="Horizon_ComesFromConfiguration_NotAHardCodedThree"/> is what proves the setting is actually bound
/// and not merely present in the file.</para>
///
/// <para><b>Rank band 360–369</b>, see <c>FilterSortTodoListTests</c> for why every file claims one.</para>
/// </summary>
[Collection("Postgres")]
public class TodoListItemDashboardWidgetTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    private const long UserId = FakeLoggedUserService.TestUserId;
    private const string WidgetRoute = "/api/todo-list-item/dashboard-widget";

    /// <summary>UTC+14 — well ahead of any plausible host, so the user's date is the host's or the one after.</summary>
    private static readonly TimeZoneInfo FarEast = TimeZoneInfo.FindSystemTimeZoneById("Pacific/Kiritimati");

    /// <summary>UTC−11 — well behind any plausible host, so the user's date is the host's or the one before.</summary>
    private static readonly TimeZoneInfo FarWest = TimeZoneInfo.FindSystemTimeZoneById("Pacific/Niue");

    private long _listId;
    private long _priorityId;

    /// <summary>
    /// The horizon in whole days, from the host's own configuration. Every expected date below is derived from
    /// it and from the caller's current date — never from a literal — so this suite states the rule rather than
    /// today's numbers.
    /// </summary>
    private int HorizonDays
    {
        get
        {
            using var scope = Fixture.UnauthenticatedFactory.Services.CreateScope();
            return scope.ServiceProvider.GetRequiredService<IOptions<TodoListSettings>>().Value.DashboardHorizonDays;
        }
    }

    /// <summary>The baseline test user is on UTC, so this is their own date for every case that does not move it.</summary>
    private static DateOnly UtcToday => DateOnly.FromDateTime(DateTime.UtcNow);

    protected override async Task SeedAsync(DbContext db)
    {
        _priorityId = await TodoListTestSeedHelper.SeedTaskPriorityAsync(db, 360, UserId, "widget", CancellationToken);
        _listId = await TodoListTestSeedHelper.SeedTodoListAsync(db, UserId, "Widget list", ct: CancellationToken);
    }

    /// <summary>
    /// The boundary itself. <c>+HorizonDays</c> is inside the window and <c>+HorizonDays + 1</c> is outside it,
    /// which is the pair an off-by-one gets wrong in one direction or the other; and the undated item pins
    /// <c>DueDate.HasValue</c>, because a task with no due date is not "due soon" — treating a null as
    /// <c>DateOnly.MinValue</c> would sweep every undated task in the account into the widget.
    /// </summary>
    [Fact(DisplayName = "TEST-5: the window is inclusive at today + the configured horizon, exclusive the day after, and excludes undated items")]
    public async Task Window_IsInclusiveAtTheHorizon_AndExcludesTheDayAfterAndUndatedItems()
    {
        var horizon = HorizonDays;

        await SeedItemAsync("due-today", UtcToday);
        await SeedItemAsync("due-tomorrow", UtcToday.AddDays(1));
        await SeedItemAsync("due-on-the-horizon", UtcToday.AddDays(horizon));
        await SeedItemAsync("due-past-the-horizon", UtcToday.AddDays(horizon + 1));
        await SeedItemAsync("undated", dueDate: null);

        var names = await WidgetNamesAsync();

        names.Should().BeEquivalentTo(["due-today", "due-tomorrow", "due-on-the-horizon"],
            $"the widget looks {horizon} days ahead inclusively -- the item due on day {horizon + 1} is past the " +
            "window and the undated one was never due at all");
    }

    /// <summary>
    /// <c>DueDate &lt;= today + horizon</c> has no lower bound, so anything overdue is in. That is right for a
    /// "what needs attention" widget, but nothing in the code says so — this is the test a future filter change
    /// has to argue with.
    /// </summary>
    [Fact(DisplayName = "TEST-5: the widget is due soon or overdue -- an item a month past its due date still appears")]
    public async Task OverdueItems_AreIncluded()
    {
        await SeedItemAsync("overdue", UtcToday.AddDays(-30));
        await SeedItemAsync("due-today", UtcToday);

        var names = await WidgetNamesAsync();

        names.Should().BeEquivalentTo(["overdue", "due-today"],
            "the window is open-ended below its end date -- a task whose due date has passed is exactly what a " +
            "dashboard widget must not hide");
    }

    /// <summary>
    /// The other half of the filter. Seeded done, so <c>TodoListItemCompletionInterceptor</c> stamps
    /// <c>CompletedTimestamp</c> on the way in — irrelevant here, and deliberately not fought.
    /// </summary>
    [Fact(DisplayName = "TEST-5: a completed item due today is absent whatever its due date says")]
    public async Task DoneItems_AreExcluded()
    {
        await SeedItemAsync("done-today", UtcToday, isDone: true);
        await SeedItemAsync("done-overdue", UtcToday.AddDays(-2), isDone: true);
        await SeedItemAsync("still-open", UtcToday);

        var names = await WidgetNamesAsync();

        names.Should().Equal("still-open");
    }

    /// <summary>
    /// Seeded in an order that is neither the answer nor its reverse, so "ordered by DueDate" cannot be confused
    /// with "ordered by id" or with an accident. <c>Sort</c> overrides the base's <c>OrderBy(Id)</c>, and nothing
    /// downstream re-sorts.
    /// </summary>
    [Fact(DisplayName = "TEST-5: rows come back ordered by DueDate ascending, not by id")]
    public async Task Items_AreOrderedByDueDateAscending()
    {
        var horizon = HorizonDays;

        await SeedItemAsync("third", UtcToday.AddDays(horizon));
        await SeedItemAsync("first", UtcToday);
        await SeedItemAsync("second", UtcToday.AddDays(1));

        var items = await ReadWidgetAsync();

        items.Select(i => i.Activity.Name).Should().Equal("first", "second", "third");
        items.Select(i => i.DueDate).Should().BeInAscendingOrder();
    }

    /// <summary>
    /// The endpoint itself never mentions the caller. This is the test that proves the global
    /// <c>IEntityWithUser</c> filter still reaches a query the endpoint has already narrowed — and the positive
    /// control rules out an empty answer passing by accident.
    /// </summary>
    [Fact(DisplayName = "TEST-5: another user's due-today item does not reach the caller's widget")]
    public async Task AnotherUsersItems_AreNotReturned()
    {
        await SeedItemAsync("mine", UtcToday);

        await using (var db = CreateDbContext())
        {
            await ReminderSeedHelper.EnsureOtherUserAsync(db, CancellationToken);

            var theirPriorityId = await TodoListTestSeedHelper.SeedTaskPriorityAsync(
                db, 361, ReminderSeedHelper.OtherUserId, "theirs", CancellationToken);
            var theirListId = await TodoListTestSeedHelper.SeedTodoListAsync(
                db, ReminderSeedHelper.OtherUserId, "Theirs list", ct: CancellationToken);
            var theirActivityId = await PlanningTestSeedHelper.SeedActivityAsync(
                db, "theirs", ReminderSeedHelper.OtherUserId, CancellationToken);

            await TodoListTestSeedHelper.SeedTodoListItemAsync(
                db, theirActivityId, theirPriorityId, theirListId, ReminderSeedHelper.OtherUserId,
                ct: CancellationToken, dueDate: UtcToday);
        }

        var names = await WidgetNamesAsync();

        names.Should().Equal("mine");
    }

    [Fact(DisplayName = "TEST-5: the widget is 401 unauthenticated")]
    public async Task Widget_RequiresAuthentication()
    {
        var response = await CreateUnauthenticatedClient().GetAsync(WidgetRoute, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// The endpoint declares no <c>Roles(...)</c>, so it inherits User + Admin + Root from the global
    /// configurator in <c>Program.cs</c>. That inheritance is invisible at the call site, and narrowing it by
    /// accident would lock every ordinary user out of their own dashboard with a 403 no test would see.
    /// </summary>
    [Fact(DisplayName = "TEST-5: a plain User-role caller gets 200 -- the widget inherits User + Admin + Root")]
    public async Task Widget_IsOpenToThePlainUserRole()
    {
        var response = await CreateUserRoleClient().GetAsync(WidgetRoute, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>
    /// <c>CQ-18</c>, east. The user is on UTC+14, so their own date is the host's or the one after it. An item
    /// due on <b>their</b> horizon day is inside the window; the day after it is not. Both assertions are made
    /// against the user's calendar, which is the only calendar the answer may depend on — a server-local or UTC
    /// bound drops the first row for the fourteen hours a day the two dates disagree.
    /// </summary>
    [Fact(DisplayName = "CQ-18: for a user east of the host, the horizon closes on the user's date, not the server's")]
    public async Task Horizon_ForAUserEastOfTheHost_IsBoundedInTheirOwnZone()
    {
        var horizon = HorizonDays;
        var theirToday = await SetUserZoneAsync(FarEast);

        await SeedItemAsync("their-horizon-day", theirToday.AddDays(horizon));
        await SeedItemAsync("day-after-their-horizon", theirToday.AddDays(horizon + 1));

        var names = await WidgetNamesAsync();

        names.Should().Contain("their-horizon-day",
            "the last day of the window is the user's own -- reading the horizon off the host's date drops a " +
            "task on its due date for every user ahead of the server");
        names.Should().NotContain("day-after-their-horizon",
            "the window still closes after the configured number of the user's days");
    }

    /// <summary>
    /// <c>CQ-18</c>, west. The mirror: on UTC−11 the user's date is the host's or the one before it, so an item
    /// on their day after the horizon is still inside a UTC-bounded window for eleven hours a day. It must be
    /// absent, and the item on their horizon day must be present — one assertion alone would pass on an endpoint
    /// that simply returned nothing.
    /// </summary>
    [Fact(DisplayName = "CQ-18: for a user west of the host, an item past their horizon is absent even while it is inside the server's")]
    public async Task Horizon_ForAUserWestOfTheHost_IsBoundedInTheirOwnZone()
    {
        var horizon = HorizonDays;
        var theirToday = await SetUserZoneAsync(FarWest);

        await SeedItemAsync("their-horizon-day", theirToday.AddDays(horizon));
        await SeedItemAsync("day-after-their-horizon", theirToday.AddDays(horizon + 1));

        var names = await WidgetNamesAsync();

        names.Should().Contain("their-horizon-day");
        names.Should().NotContain("day-after-their-horizon",
            "this date is still within the horizon counted from the host's date for most of the day -- showing " +
            "it is the west-of-the-server half of CQ-18, a task appearing a day early");
    }

    /// <summary>
    /// The <c>3</c> used to be an unexplained literal in the filter. Overriding the bound setting must move the
    /// window, which is the only way to tell a bound option from a defaulted one — <c>DashboardHorizonDays</c>
    /// has an initialiser, so a section that never reached the endpoint would still answer 3 and look correct.
    /// </summary>
    [Fact(DisplayName = "DOC-1: the horizon comes from TodoListSettings -- overriding it moves the window")]
    public async Task Horizon_ComesFromConfiguration_NotAHardCodedThree()
    {
        await SeedItemAsync("due-today", UtcToday);
        await SeedItemAsync("due-tomorrow", UtcToday.AddDays(1));

        await using var factory = CreateFactory(TestRoles.User, UserId, configOverrides:
            [new KeyValuePair<string, string?>("TodoListSettings:DashboardHorizonDays", "0")]);

        var names = await WidgetNamesAsync(factory.CreateClient());

        // ContainSingle().Which rather than Equal(): the params-string[] overload would swallow the reason
        // as a second expected element.
        names.Should().ContainSingle(
            "a horizon of zero days is today only -- if tomorrow's task is still here the endpoint is not " +
            "reading the setting at all")
            .Which.Should().Be("due-today");
    }

    /// <summary>
    /// The widget shares <c>TodoListItemResponse.Projection</c> with the list read, and a field dropped from
    /// that projection reads back as null rather than failing — indistinguishable from "not set", as
    /// <c>TodoListItemPairingTests</c> already notes for the pairing id. These three are the ones the widget
    /// renders and the list read would not miss.
    /// </summary>
    [Fact(DisplayName = "TEST-5: the widget's rows carry TaskPriority, Steps and PairedLeisureActivityId")]
    public async Task Projection_CarriesTheFieldsTheWidgetRenders()
    {
        long leisureActivityId;
        await using (var db = CreateDbContext())
            leisureActivityId = await PlanningTestSeedHelper.SeedActivityAsync(db, "Play guitar", UserId, CancellationToken);

        await SeedItemAsync("fully-populated", UtcToday,
            pairedLeisureActivityId: leisureActivityId,
            steps:
            [
                new TodoListStep { Name = "Open the draft", Order = 0 },
                new TodoListStep { Name = "Send it", Order = 1, IsDone = true }
            ]);

        var item = (await ReadWidgetAsync()).Should().ContainSingle().Subject;

        item.TaskPriority.Should().NotBeNull();
        item.TaskPriority.Priority.Should().Be(360, "the rank is what the widget colours and groups by");
        item.Steps.Select(s => s.Name).Should().Equal("Open the draft", "Send it");
        item.PairedLeisureActivityId.Should().Be(leisureActivityId,
            "a dropped pairing id reads back as null, which is exactly what an unpaired task looks like");
    }

    // ---------------------------------------------------------------- helpers

    /// <summary>
    /// One activity per item — <c>todo_list_item</c> is unique on <c>(UserId, ActivityId, TodoListId)</c>, and
    /// the activity's name is how every case above identifies its rows.
    /// </summary>
    private async Task<long> SeedItemAsync(string name, DateOnly? dueDate, bool isDone = false,
        long? pairedLeisureActivityId = null, List<TodoListStep>? steps = null)
    {
        await using var db = CreateDbContext();

        var activityId = await PlanningTestSeedHelper.SeedActivityAsync(db, name, UserId, CancellationToken);

        return await TodoListTestSeedHelper.SeedTodoListItemAsync(
            db, activityId, _priorityId, _listId, UserId, steps: steps, isDone: isDone,
            ct: CancellationToken, dueDate: dueDate, pairedLeisureActivityId: pairedLeisureActivityId);
    }

    /// <summary>
    /// Moves the caller into <paramref name="zone"/> and returns the date it is there now — the calendar every
    /// assertion in the two <c>CQ-18</c> cases is stated against. The fixture reseeds the user on UTC before
    /// each test, so this affects nothing else.
    /// </summary>
    private async Task<DateOnly> SetUserZoneAsync(TimeZoneInfo zone)
    {
        await using var db = CreateDbContext();

        var user = await db.Set<User>().IgnoreQueryFilters().SingleAsync(u => u.Id == UserId, CancellationToken);
        user.Timezone = zone;
        await db.SaveChangesAsync(CancellationToken);

        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, zone));
    }

    private async Task<List<TodoListItemResponse>> ReadWidgetAsync(HttpClient? client = null)
    {
        var response = await (client ?? CreateClient()).GetAsync(WidgetRoute, CancellationToken);

        var body = await response.Content.ReadAsStringAsync(CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);

        return JsonSerializer.Deserialize<List<TodoListItemResponse>>(body, JsonOpts)!;
    }

    private async Task<List<string>> WidgetNamesAsync(HttpClient? client = null)
        => (await ReadWidgetAsync(client)).Select(i => i.Activity.Name).ToList();
}
