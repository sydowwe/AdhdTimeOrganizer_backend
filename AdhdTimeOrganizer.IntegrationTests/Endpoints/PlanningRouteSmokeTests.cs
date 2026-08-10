using System.Net;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using AdhdTimeOrganizer.Planning.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.TodoLists.domain.model.entity.todoList;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Sydowwe.Framework.Testing;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Endpoints;

/// <summary>
/// The Planning counterpart of <see cref="CoreRouteSmokeTests"/>. Same reasoning: the slice's
/// endpoints only route if <c>AdhdTimeOrganizer.Planning</c> is in the FastEndpoints
/// <c>o.Assemblies</c> list in <c>Program.cs</c> (<c>DisableAutoDiscovery = true</c>), and leaving it
/// out is not a build error — every planner, calendar and reminder route simply 404s.
/// <para>
/// Seeder double-registration is covered globally by
/// <c>CoreRouteSmokeTests.CoreSeeders_AreRegisteredExactlyOnce</c>, which asserts over every
/// registered seeder rather than Core's alone, so this slice needs no separate copy of it.
/// </para>
/// </summary>
[Collection("Postgres")]
public class PlanningRouteSmokeTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    /// <summary>
    /// One route per family that moved into the slice. <c>/planner/settings</c> and
    /// <c>/reminder/by-date</c> are hand-written routes; the other two come from the Framework base
    /// classes, which derive them by kebaberizing the entity name.
    /// </summary>
    [Theory]
    [InlineData("/api/planner/settings")]
    [InlineData("/api/task-importance")]
    [InlineData("/api/task-planner-day-template/all-options/")]
    [InlineData("/api/reminder/by-date/2026-08-11")]
    public async Task PlanningRoutes_AreRegistered(string route)
    {
        var client = CreateUserRoleClient();

        var response = await client.GetAsync(route, TestContext.Current.CancellationToken);

        response.StatusCode.Should().NotBe(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// The Planning extraction deleted <c>PlannerTask.TodolistItem</c> and moved the relationship into
    /// <c>AppDbContext.ConfigureCrossSliceRelationships</c>, so that Planning need not reference
    /// TodoLists. That move is invisible to the compiler in both directions: forget the host-side
    /// declaration and the FK silently disappears from the model — planner tasks would keep a dangling
    /// <c>todolist_item_id</c> and a deleted to-do item would no longer null it out. This asserts the
    /// relationship still exists, still points at <c>todo_list_item</c>, still nulls on delete, and
    /// still carries the pinned constraint name that keeps it out of every future migration.
    /// </summary>
    [Fact]
    public void PlannerTaskToTodoListItem_ForeignKey_SurvivesTheSliceSplit()
    {
        using var db = CreateDbContext();

        var plannerTask = db.Model.FindEntityType(typeof(PlannerTask))!;
        var fk = plannerTask.GetForeignKeys()
            .SingleOrDefault(f => f.PrincipalEntityType.ClrType == typeof(TodoListItem));

        fk.Should().NotBeNull("PlannerTask.TodolistItemId is declared in AppDbContext.ConfigureCrossSliceRelationships");
        fk!.Properties.Should().ContainSingle().Which.Name.Should().Be(nameof(PlannerTask.TodolistItemId));
        fk.DeleteBehavior.Should().Be(DeleteBehavior.SetNull);
        fk.GetConstraintName().Should().Be("fk_planner_task_todo_list_items_todolist_item_id");

        // No navigation on either end — re-adding one is what would drag the project reference back.
        fk.DependentToPrincipal.Should().BeNull();
        fk.PrincipalToDependent.Should().BeNull();
    }
}
