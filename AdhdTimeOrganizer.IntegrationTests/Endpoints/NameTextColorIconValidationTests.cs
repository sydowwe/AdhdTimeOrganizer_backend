using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AdhdTimeOrganizer.Core.domain.model.entity.activity;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using AdhdTimeOrganizer.TodoLists.domain.model.entity.todoList;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.domain;
using Sydowwe.Framework.domain.@enum;
using Sydowwe.Framework.Testing;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Endpoints;

/// <summary>
/// The two field contracts shared by every route behind <c>NameTextColorIconValidator</c> — activity
/// role, activity category and to-do-list category — on both POST and PUT.
/// </summary>
/// <remarks>
/// <para>
/// <b>CQ-1 — Icon.</b> The validator's limit and the column it writes into have to be the same number.
/// The failure this pins is not "the request is refused"; it is <b>where</b>. A limit above the column
/// length lets the request through to Postgres, which raises 22001 and reaches the client through
/// <c>DbUtils.HandleException</c> as a generic 4xx with no per-field error — so a status-only assertion
/// passes while the form has nothing to attach to the <c>icon</c> input. Every rejection below therefore
/// asserts on the <b>error key</b>, not on the status alone.
/// </para>
/// <para>
/// <b>B10 — Color.</b> <c>color</c> is a <see cref="ColorPalette"/> <i>name</i>, never a hex string.
/// The client's <c>ColorPicker</c> writes names and resolves them to a theme-aware pair, and every
/// seeder stores names; the validator used to demand <c>#RRGGBB</c>, and because these tests posted
/// <c>#abcdef</c> the two sides disagreed for three months without a red test. The palette cases post
/// what the client posts, and the longest name (<c>burgundy</c>, 8 characters — one more than the old
/// column) is asserted to round-trip whole.
/// </para>
/// </remarks>
[Collection("Postgres")]
public class NameTextColorIconValidationTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    private const string RoleRoute = "api/activity-role";
    private const string CategoryRoute = "api/activity-category";
    private const string TodoListCategoryRoute = "api/todo-list-category";

    private const string ValidColor = ColorPalette.Blue;

    public static TheoryData<string> Routes => new(RoleRoute, CategoryRoute, TodoListCategoryRoute);

    public static TheoryData<string, string> RoutesByColor
    {
        get
        {
            var data = new TheoryData<string, string>();
            foreach (var route in new[] { RoleRoute, CategoryRoute, TodoListCategoryRoute })
            foreach (var color in new[] { ColorPalette.Burgundy, ColorPalette.Sky, ColorPalette.Slate })
            {
                data.Add(route, color);
            }

            return data;
        }
    }

    public static TheoryData<string, string> RoutesByRejectedColor
    {
        get
        {
            var data = new TheoryData<string, string>();
            foreach (var route in new[] { RoleRoute, CategoryRoute, TodoListCategoryRoute })
            foreach (var color in new[] { "#abcdef", "#fff", "default", "", "Blue", "cornflower" })
            {
                data.Add(route, color);
            }

            return data;
        }
    }

    // ----- B10: Color is a palette name -------------------------------------------------------------

    [Theory(DisplayName = "B10: a palette name is accepted and stored whole — including the 8-character one")]
    [MemberData(nameof(RoutesByColor))]
    public async Task Create_PaletteName_Returns201AndStoresIt(string route, string color)
    {
        var name = $"Palette {color} {Guid.NewGuid():N}";

        var response = await CreateClient().PostAsJsonAsync(route,
            new { Name = name, Color = color }, JsonOpts, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Created,
            await response.Content.ReadAsStringAsync(CancellationToken));

        (await ColorByNameAsync(route, name))
            .Should().Be(color, "the stored value is what the client's ColorPicker will resolve on the next read");
    }

    /// <summary>
    /// Every name the server declares has to survive a create — this is the whole contract. Posting
    /// all 29 to one route is enough: the rule is shared, and <see cref="RoutesByColor"/> covers the
    /// per-route wiring.
    /// </summary>
    [Fact(DisplayName = "B10: every ColorPalette name is accepted")]
    public async Task Create_EveryPaletteName_Returns201()
    {
        var client = CreateClient();

        foreach (var color in ColorPalette.All)
        {
            var response = await client.PostAsJsonAsync(RoleRoute,
                new { Name = $"All {color} {Guid.NewGuid():N}", Color = color }, JsonOpts, CancellationToken);

            response.StatusCode.Should().Be(HttpStatusCode.Created,
                $"'{color}' is in ColorPalette.All, so it must be accepted: {await response.Content.ReadAsStringAsync(CancellationToken)}");
        }
    }

    /// <summary>
    /// The validator no longer carries a length rule for colour — the palette bounds it — so this is
    /// what keeps a future swatch from being accepted by the validator and truncated by the column.
    /// </summary>
    [Fact(DisplayName = "B10: every ColorPalette name fits the column")]
    public void EveryPaletteName_FitsTheColorColumn()
    {
        ColorPalette.All.Should().OnlyContain(c => c.Length <= EntityFieldLengths.Color,
            "a name longer than EntityFieldLengths.Color would pass the validator and fail in Postgres with 22001");
    }

    [Theory(DisplayName = "B10: a hex code, the picker's 'default', an empty string or an unknown name is a 400 naming color")]
    [MemberData(nameof(RoutesByRejectedColor))]
    public async Task Create_NotAPaletteName_Returns400NamingColor(string route, string color)
    {
        var name = $"Refused {Guid.NewGuid():N}";

        var response = await CreateClient().PostAsJsonAsync(route,
            new { Name = name, Color = color }, JsonOpts, CancellationToken);

        var body = await ShouldBeValidationErrorFor(response, "color");
        body.Should().Contain("palette", "the message must tell the client what the accepted set is");

        (await CountByNameAsync(route, name))
            .Should().Be(0, "a rejected create must not leave a row behind");
    }

    /// <summary>
    /// The seeded rows carry palette names, so an unchanged round trip through the edit dialog has
    /// to succeed — that is the exact shape of the failure observed on <i>Health &amp; Fitness</i>.
    /// </summary>
    [Theory(DisplayName = "B10: PUT accepts a palette name and rejects hex the same way POST does")]
    [MemberData(nameof(Routes))]
    public async Task Update_PaletteNameAccepted_HexRefused_AndTheRowKeepsItsColor(string route)
    {
        var name = $"Color Update {Guid.NewGuid():N}";
        var id = await CreateAsync(route, name, ColorPalette.Emerald);

        var unchanged = await CreateClient().PutAsJsonAsync($"{route}/{id}",
            new { Name = name, Color = ColorPalette.Emerald }, JsonOpts, CancellationToken);
        unchanged.StatusCode.Should().Be(HttpStatusCode.OK,
            $"a no-change edit of a seeded row must not be refused: {await unchanged.Content.ReadAsStringAsync(CancellationToken)}");

        var recoloured = await CreateClient().PutAsJsonAsync($"{route}/{id}",
            new { Name = name, Color = ColorPalette.Burgundy }, JsonOpts, CancellationToken);
        recoloured.StatusCode.Should().Be(HttpStatusCode.OK,
            await recoloured.Content.ReadAsStringAsync(CancellationToken));
        (await ColorByNameAsync(route, name)).Should().Be(ColorPalette.Burgundy);

        var hex = await CreateClient().PutAsJsonAsync($"{route}/{id}",
            new { Name = name, Color = "#abcdef" }, JsonOpts, CancellationToken);
        await ShouldBeValidationErrorFor(hex, "color");

        (await ColorByNameAsync(route, name))
            .Should().Be(ColorPalette.Burgundy, "a rejected update must leave the stored colour untouched");
    }

    // ----- CQ-1: Icon length equals the column ------------------------------------------------------

    [Theory(DisplayName = "CQ-1: an icon longer than the column is a 400 naming the field")]
    [InlineData(RoleRoute, 51)]
    [InlineData(RoleRoute, 60)]
    [InlineData(CategoryRoute, 51)]
    [InlineData(CategoryRoute, 60)]
    public async Task Create_IconLongerThanTheColumn_Returns400NamingIcon(string route, int iconLength)
    {
        var name = $"Icon Too Long {Guid.NewGuid():N}";

        var response = await CreateClient().PostAsJsonAsync(route,
            new { Name = name, Color = ValidColor, Icon = new string('x', iconLength) }, JsonOpts, CancellationToken);

        await ShouldBeValidationErrorFor(response, "icon");

        (await CountByNameAsync(route, name))
            .Should().Be(0, "a rejected create must not leave a row behind");
    }

    /// <summary>
    /// The boundary on the accepting side, so the fix cannot be over-tightened to 49 or to some
    /// arbitrary smaller value — and so the row is checked to actually hold all 50 characters rather
    /// than a silently truncated prefix.
    /// </summary>
    [Theory(DisplayName = "CQ-1: an icon exactly the length of the column is accepted")]
    [InlineData(RoleRoute)]
    [InlineData(CategoryRoute)]
    public async Task Create_IconAtTheColumnLength_Returns201AndStoresIt(string route)
    {
        var name = $"Icon At Limit {Guid.NewGuid():N}";
        var icon = new string('x', 50);

        var response = await CreateClient().PostAsJsonAsync(route,
            new { Name = name, Color = ValidColor, Icon = icon }, JsonOpts, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Created,
            await response.Content.ReadAsStringAsync(CancellationToken));

        (await IconByNameAsync(route, name))
            .Should().Be(icon, "50 is the column length, so it must round-trip whole rather than be truncated");
    }

    /// <summary>
    /// Create and update share the validator but reach it through different <c>Configure()</c>
    /// overrides, so a fix applied to one and not the other is a plausible half-fix.
    /// </summary>
    [Theory(DisplayName = "CQ-1: PUT rejects an over-length icon the same way POST does")]
    [InlineData(RoleRoute)]
    [InlineData(CategoryRoute)]
    public async Task Update_IconLongerThanTheColumn_Returns400NamingIcon_AndLeavesTheRowAlone(string route)
    {
        var name = $"Icon Update {Guid.NewGuid():N}";
        var id = await CreateAsync(route, name, ValidColor, "fas fa-check");

        var response = await CreateClient().PutAsJsonAsync($"{route}/{id}",
            new { Name = name, Color = ValidColor, Icon = new string('x', 60) }, JsonOpts, CancellationToken);

        await ShouldBeValidationErrorFor(response, "icon");

        (await IconByNameAsync(route, name))
            .Should().Be("fas fa-check", "a rejected update must leave the stored icon untouched");
    }

    // ----- helpers ---------------------------------------------------------------------------------

    private async Task<long> CreateAsync(string route, string name, string color, string? icon = null)
    {
        var created = await CreateClient().PostAsJsonAsync(route,
            new { Name = name, Color = color, Icon = icon }, JsonOpts, CancellationToken);
        created.StatusCode.Should().Be(HttpStatusCode.Created,
            await created.Content.ReadAsStringAsync(CancellationToken));
        return await created.Content.ReadFromJsonAsync<long>(JsonOpts, CancellationToken);
    }

    private async Task<string> ShouldBeValidationErrorFor(HttpResponseMessage response, string property)
    {
        var body = await response.Content.ReadAsStringAsync(CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest, body);

        var root = JsonDocument.Parse(body).RootElement;
        root.TryGetProperty("errors", out var errors).Should().BeTrue(body);

        errors.EnumerateObject().Select(e => e.Name)
            .Should().Contain(name => string.Equals(name, property, StringComparison.OrdinalIgnoreCase),
                $"the validator keys the failure to {property}; a Postgres 22001 reaching the client does not, "
                + "so the form has no input to attach the message to");

        return body;
    }

    private async Task<int> CountByNameAsync(string route, string name)
    {
        await using var db = CreateDbContext();
        return route switch
        {
            RoleRoute => await db.Set<ActivityRole>().IgnoreQueryFilters().CountAsync(r => r.Name == name, CancellationToken),
            CategoryRoute => await db.Set<ActivityCategory>().IgnoreQueryFilters().CountAsync(c => c.Name == name, CancellationToken),
            _ => await db.Set<TodoListCategory>().IgnoreQueryFilters().CountAsync(c => c.Name == name, CancellationToken)
        };
    }

    private async Task<string?> IconByNameAsync(string route, string name)
    {
        await using var db = CreateDbContext();
        return route == RoleRoute
            ? (await db.Set<ActivityRole>().IgnoreQueryFilters().SingleAsync(r => r.Name == name, CancellationToken)).Icon
            : (await db.Set<ActivityCategory>().IgnoreQueryFilters().SingleAsync(c => c.Name == name, CancellationToken)).Icon;
    }

    private async Task<string> ColorByNameAsync(string route, string name)
    {
        await using var db = CreateDbContext();
        return route switch
        {
            RoleRoute => (await db.Set<ActivityRole>().IgnoreQueryFilters().SingleAsync(r => r.Name == name, CancellationToken)).Color,
            CategoryRoute => (await db.Set<ActivityCategory>().IgnoreQueryFilters().SingleAsync(c => c.Name == name, CancellationToken)).Color,
            _ => (await db.Set<TodoListCategory>().IgnoreQueryFilters().SingleAsync(c => c.Name == name, CancellationToken)).Color
        };
    }
}
