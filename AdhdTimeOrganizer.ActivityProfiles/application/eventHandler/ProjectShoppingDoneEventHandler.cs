using AdhdTimeOrganizer.ActivityProfiles.domain.model.entity;
using AdhdTimeOrganizer.ActivityProfiles.domain.model.@enum;
using AdhdTimeOrganizer.Core.application.@event;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;

namespace AdhdTimeOrganizer.ActivityProfiles.application.eventHandler;

/// <summary>
/// A project becomes suggestible again once its shopping is done (FO13): ticking the to-do item the
/// shopping handoff created moves a <see cref="ReadinessStatus.NeedsShopping"/> project to
/// <see cref="ReadinessStatus.ReadyToStart"/>, which every leisure draw excludes until then.
/// </summary>
/// <remarks>
/// <para>
/// Lives here, not in the host: the publisher is TodoLists, but the only row this writes is an
/// <see cref="ActivityProjectProfile"/>. The join is <see cref="ActivityProjectProfile.ShoppingTodoListItemId"/>,
/// the navigation-free FK the host declares — never the shared activity, which would also fire on a
/// "work on the bookshelf" to-do for the same project.
/// </para>
/// <para>
/// Only forwards, and only out of <c>NeedsShopping</c>. Un-ticking does not move the project back (the
/// materials are bought), a project still in <c>Planning</c> is not the user's to skip ahead, and one already
/// <c>ReadyToStart</c> needs nothing — so the handler is idempotent under the double publish a step toggle and
/// an item toggle can produce. Item-done rather than all-steps-done, because that is how the user closes it.
/// </para>
/// <para>
/// An archived project is not moved: the host's archival filter hides it here, and this slice cannot name the
/// filter to lift it. It is off the board and out of every draw anyway.
/// </para>
/// </remarks>
public class ProjectShoppingDoneEventHandler(
    IServiceScopeFactory scopeFactory,
    ILogger<ProjectShoppingDoneEventHandler> logger) : IEventHandler<TodoListItemIsDoneChangedEvent>
{
    public async Task HandleAsync(TodoListItemIsDoneChangedEvent eventModel, CancellationToken ct)
    {
        if (!eventModel.NewIsDone)
            return;

        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();

        try
        {
            // Scoped on the event's UserId by hand. The profiles are not IEntityWithUser, and the host's profile
            // filter escapes to "every row" when there is no principal (ScopeUserId == null), which is the case
            // in this scope.
            var projects = await dbContext.Set<ActivityProjectProfile>()
                .Where(p => p.ShoppingTodoListItemId == eventModel.TodoListItemId
                            && p.Activity.UserId == eventModel.UserId
                            && p.ReadinessStatus == ReadinessStatus.NeedsShopping)
                .ToListAsync(ct);

            if (projects.Count == 0)
                return;

            foreach (var project in projects)
                project.ReadinessStatus = ReadinessStatus.ReadyToStart;

            await dbContext.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The toggle has already committed and awaits this via Mode.WaitForAll, so a failure here must not
            // turn the user's tick into a 500. Same contract as every other seam-event handler.
            logger.LogError(ex, "Failed to mark the shopping project ready for TodoListItem {TodoListItemId} of user {UserId}",
                eventModel.TodoListItemId, eventModel.UserId);
        }
    }
}
