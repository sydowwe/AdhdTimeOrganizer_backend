using System.Net;
using System.Net.Http.Json;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using AdhdTimeOrganizer.IntegrationTests.Reminders;
using AdhdTimeOrganizer.Planning.application.dto.response.taskPlanner.template;
using AdhdTimeOrganizer.Planning.domain.model.entity.activityPlanning;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.Testing;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Endpoints;

/// <summary>
/// B5 / item 1 — pinned day-plan templates. The pin used to live in one browser's <c>localStorage</c>, so the
/// thing worth pinning down here is that it is now a property of the template <i>as the caller sees it</i>:
/// it comes back on the ordinary <c>fetchAll</c>, it survives an unrelated edit of the same template, and it
/// cannot be set on someone else's row.
/// </summary>
[Collection("Postgres")]
public class TaskPlannerDayTemplatePinTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    private const string TemplateUrl = "api/task-planner-day-template";

    [Fact(DisplayName = "Pinning shows up on the next fetchAll, and pinned templates lead the list")]
    public async Task Pin_IsVisibleOnFetchAll_AndSortsFirst()
    {
        long pinnedId;
        long otherId;
        await using (var db = CreateDbContext())
        {
            // "aaa" sorts before "zzz" on the pre-existing name tiebreaker, so pinning "zzz" is only
            // observable if IsPinned is actually the first sort key.
            otherId = await PlanningTestSeedHelper.SeedTaskPlannerDayTemplateAsync(db, name: "aaa-unpinned", ct: CancellationToken);
            pinnedId = await PlanningTestSeedHelper.SeedTaskPlannerDayTemplateAsync(db, name: "zzz-pinned", ct: CancellationToken);
        }

        var client = CreateClient();
        var patch = await client.PatchAsJsonAsync($"{TemplateUrl}/{pinnedId}/pinned", new { IsPinned = true }, JsonOpts, CancellationToken);
        patch.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var templates = await FetchAllAsync(client);

        templates.Single(t => t.Id == pinnedId).IsPinned.Should().BeTrue();
        templates.Single(t => t.Id == otherId).IsPinned.Should().BeFalse();
        templates.First().Id.Should().Be(pinnedId, "pinned templates lead the list");
    }

    [Fact(DisplayName = "The pin is set, not toggled — pinning twice leaves it pinned")]
    public async Task Pin_IsAbsolute_NotAToggle()
    {
        long templateId;
        await using (var db = CreateDbContext())
            templateId = await PlanningTestSeedHelper.SeedTaskPlannerDayTemplateAsync(db, ct: CancellationToken);

        var client = CreateClient();
        await client.PatchAsJsonAsync($"{TemplateUrl}/{templateId}/pinned", new { IsPinned = true }, JsonOpts, CancellationToken);
        await client.PatchAsJsonAsync($"{TemplateUrl}/{templateId}/pinned", new { IsPinned = true }, JsonOpts, CancellationToken);

        await using var verify = CreateDbContext();
        var stored = await verify.Set<TaskPlannerDayTemplate>().AsNoTracking()
            .SingleAsync(t => t.Id == templateId, CancellationToken);
        stored.IsPinned.Should().BeTrue("two devices pinning the same template must converge, not flip it back");

        var unpin = await client.PatchAsJsonAsync($"{TemplateUrl}/{templateId}/pinned", new { IsPinned = false }, JsonOpts, CancellationToken);
        unpin.StatusCode.Should().Be(HttpStatusCode.NoContent);

        await using var verifyAgain = CreateDbContext();
        (await verifyAgain.Set<TaskPlannerDayTemplate>().AsNoTracking()
            .SingleAsync(t => t.Id == templateId, CancellationToken)).IsPinned.Should().BeFalse();
    }

    [Fact(DisplayName = "Editing a template does not unpin it")]
    public async Task UpdatingTheTemplate_LeavesThePinAlone()
    {
        long templateId;
        await using (var db = CreateDbContext())
            templateId = await PlanningTestSeedHelper.SeedTaskPlannerDayTemplateAsync(db, name: "before-edit", ct: CancellationToken);

        var client = CreateClient();
        await client.PatchAsJsonAsync($"{TemplateUrl}/{templateId}/pinned", new { IsPinned = true }, JsonOpts, CancellationToken);

        // IsPinned is deliberately absent from TaskPlannerDayTemplateRequest, so an edit submitted from a form
        // that was opened before the pin cannot carry a stale "false" back.
        var update = await client.PutAsJsonAsync($"{TemplateUrl}/{templateId}", new
        {
            Name = "after-edit",
            IsActive = true,
            SuggestedForDayType = "Workday",
            ScheduledDays = Array.Empty<string>(),
            Tags = Array.Empty<string>()
        }, JsonOpts, CancellationToken);
        update.IsSuccessStatusCode.Should().BeTrue();

        await using var verify = CreateDbContext();
        var stored = await verify.Set<TaskPlannerDayTemplate>().AsNoTracking()
            .SingleAsync(t => t.Id == templateId, CancellationToken);
        stored.Name.Should().Be("after-edit");
        stored.IsPinned.Should().BeTrue();
    }

    [Fact(DisplayName = "Another user's template is a 404, and stays unpinned")]
    public async Task PinningSomeoneElsesTemplate_IsNotFound()
    {
        long theirTemplateId;
        await using (var db = CreateDbContext())
        {
            await ReminderSeedHelper.EnsureOtherUserAsync(db, CancellationToken);
            theirTemplateId = await PlanningTestSeedHelper.SeedTaskPlannerDayTemplateAsync(
                db, ReminderSeedHelper.OtherUserId, "theirs", CancellationToken);
        }

        // 404 rather than 403: the row is invisible to this caller through the global IEntityWithUser query
        // filter, so the endpoint never sees it to forbid it.
        var response = await CreateClient().PatchAsJsonAsync(
            $"{TemplateUrl}/{theirTemplateId}/pinned", new { IsPinned = true }, JsonOpts, CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);

        await using var verify = CreateDbContext();
        var stored = await verify.Set<TaskPlannerDayTemplate>().AsNoTracking().IgnoreQueryFilters()
            .SingleAsync(t => t.Id == theirTemplateId, CancellationToken);
        stored.IsPinned.Should().BeFalse();
    }

    [Fact(DisplayName = "Pinning requires authentication")]
    public async Task Unauthenticated_Returns401()
    {
        long templateId;
        await using (var db = CreateDbContext())
            templateId = await PlanningTestSeedHelper.SeedTaskPlannerDayTemplateAsync(db, ct: CancellationToken);

        var response = await CreateUnauthenticatedClient().PatchAsJsonAsync(
            $"{TemplateUrl}/{templateId}/pinned", new { IsPinned = true }, JsonOpts, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact(DisplayName = "Deleting a pinned template takes the pin with it — no orphaned pinned id")]
    public async Task DeletingAPinnedTemplate_LeavesNothingBehind()
    {
        long templateId;
        await using (var db = CreateDbContext())
            templateId = await PlanningTestSeedHelper.SeedTaskPlannerDayTemplateAsync(db, ct: CancellationToken);

        var client = CreateClient();
        await client.PatchAsJsonAsync($"{TemplateUrl}/{templateId}/pinned", new { IsPinned = true }, JsonOpts, CancellationToken);

        var delete = await client.DeleteAsync($"{TemplateUrl}/{templateId}", CancellationToken);
        delete.IsSuccessStatusCode.Should().BeTrue();

        var templates = await FetchAllAsync(client);
        templates.Should().NotContain(t => t.Id == templateId,
            "the pin lives on the template, so a deleted template cannot leave a dangling pinned id for the client to filter");
    }

    private async Task<List<TaskPlannerDayTemplateResponse>> FetchAllAsync(HttpClient client)
    {
        var response = await client.GetAsync(TemplateUrl, CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<List<TaskPlannerDayTemplateResponse>>(JsonOpts, CancellationToken))!;
    }
}
