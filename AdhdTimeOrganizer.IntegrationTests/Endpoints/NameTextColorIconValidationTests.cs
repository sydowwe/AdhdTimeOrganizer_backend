using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AdhdTimeOrganizer.Core.domain.model.entity.activity;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.Testing;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Endpoints;

/// <summary>
/// CQ-1 — the validator's limit for <c>Icon</c> and the column it writes into have to be the same
/// number, on both routes that share <c>NameTextColorIconValidator</c>.
/// </summary>
/// <remarks>
/// <para>
/// The failure this pins is not "the request is refused". It is <b>where</b> it is refused. A limit
/// above the column length lets the request through to Postgres, which raises 22001 and reaches the
/// client through <c>DbUtils.HandleException</c> as a generic 4xx with no per-field error — so a
/// status-only assertion passes while the form has nothing to attach to the <c>icon</c> input. Every
/// rejection below therefore asserts on the <b>error key</b>, not on the status alone.
/// </para>
/// <para>
/// Both layers now read <c>EntityFieldLengths.Icon</c>, so the boundary cases (50 accepted, 51
/// rejected) are what stops the two drifting apart again in either direction.
/// </para>
/// </remarks>
[Collection("Postgres")]
public class NameTextColorIconValidationTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    private const string RoleRoute = "api/activity-role";
    private const string CategoryRoute = "api/activity-category";

    [Theory(DisplayName = "CQ-1: an icon longer than the column is a 400 naming the field")]
    [InlineData(RoleRoute, 51)]
    [InlineData(RoleRoute, 60)]
    [InlineData(CategoryRoute, 51)]
    [InlineData(CategoryRoute, 60)]
    public async Task Create_IconLongerThanTheColumn_Returns400NamingIcon(string route, int iconLength)
    {
        var name = $"Icon Too Long {Guid.NewGuid():N}";

        var response = await CreateClient().PostAsJsonAsync(route,
            new { Name = name, Color = "#abcdef", Icon = new string('x', iconLength) }, JsonOpts, CancellationToken);

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
            new { Name = name, Color = "#abcdef", Icon = icon }, JsonOpts, CancellationToken);

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
        var created = await CreateClient().PostAsJsonAsync(route,
            new { Name = name, Color = "#abcdef", Icon = "fas fa-check" }, JsonOpts, CancellationToken);
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var id = await created.Content.ReadFromJsonAsync<long>(JsonOpts, CancellationToken);

        var response = await CreateClient().PutAsJsonAsync($"{route}/{id}",
            new { Name = name, Color = "#abcdef", Icon = new string('x', 60) }, JsonOpts, CancellationToken);

        await ShouldBeValidationErrorFor(response, "icon");

        (await IconByNameAsync(route, name))
            .Should().Be("fas fa-check", "a rejected update must leave the stored icon untouched");
    }

    private async Task ShouldBeValidationErrorFor(HttpResponseMessage response, string property)
    {
        var body = await response.Content.ReadAsStringAsync(CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest, body);

        var root = JsonDocument.Parse(body).RootElement;
        root.TryGetProperty("errors", out var errors).Should().BeTrue(body);

        errors.EnumerateObject().Select(e => e.Name)
            .Should().Contain(name => string.Equals(name, property, StringComparison.OrdinalIgnoreCase),
                $"the validator keys the failure to {property}; a Postgres 22001 reaching the client does not, "
                + "so the form has no input to attach the message to");
    }

    private async Task<int> CountByNameAsync(string route, string name)
    {
        await using var db = CreateDbContext();
        return route == RoleRoute
            ? await db.Set<ActivityRole>().IgnoreQueryFilters().CountAsync(r => r.Name == name, CancellationToken)
            : await db.Set<ActivityCategory>().IgnoreQueryFilters().CountAsync(c => c.Name == name, CancellationToken);
    }

    private async Task<string?> IconByNameAsync(string route, string name)
    {
        await using var db = CreateDbContext();
        return route == RoleRoute
            ? (await db.Set<ActivityRole>().IgnoreQueryFilters().SingleAsync(r => r.Name == name, CancellationToken)).Icon
            : (await db.Set<ActivityCategory>().IgnoreQueryFilters().SingleAsync(c => c.Name == name, CancellationToken)).Icon;
    }
}
