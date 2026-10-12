using AdhdTimeOrganizer.ActivityProfiles.application.dto.request;
using AdhdTimeOrganizer.ActivityProfiles.application.validator;
using AdhdTimeOrganizer.ActivityProfiles.domain.model.entity;
using AdhdTimeOrganizer.Core.application.seam;
using AdhdTimeOrganizer.Core.domain.serviceContract;
using FastEndpoints;
using FluentValidation.Results;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.application.endpoint.@base.command;
using Sydowwe.Framework.application.extensions;

namespace AdhdTimeOrganizer.ActivityProfiles.application.endpoint.project.command;

/// <summary>
/// <c>PATCH /activity-project-profile/{id}/shopping-item</c> — remembers which to-do item the shopping
/// handoff created for this project (FO13). <c>ProjectShoppingDoneEventHandler</c> reads the link when the
/// item is ticked done.
/// </summary>
/// <remarks>
/// A second call replaces the link rather than adding one: a project has one shopping trip at a time. The
/// item's activity is not compared with the project's — the dialog only ever creates the item for the
/// project's own activity, and this slice cannot read <c>TodoListItem</c>. Ownership of the item is what
/// matters, and that goes through the <see cref="UserOwnedReferenceKeys.TodoListItem"/> verifier.
/// </remarks>
public class PatchActivityProjectProfileShoppingItemEndpoint(DbContext dbContext, IReferencedIdOwnershipService ownership)
    : BasePatchEndpoint<ActivityProjectProfile, PatchActivityProjectProfileShoppingItemRequest>(dbContext)
{
    private readonly DbContext _dbContext = dbContext;

    public override void Configure()
    {
        base.Configure();
        Patch("/activity-project-profile/{id:long}/shopping-item");
        Validator<PatchActivityProjectProfileShoppingItemValidator>();
        Summary(s =>
        {
            s.Summary = "Link a project to its shopping to-do item";
            s.Description = "Sets ShoppingTodoListItemId. Ticking that item done later moves a NeedsShopping project to ReadyToStart.";
            s.Response(204, "Success");
            s.Response(404, "Project or to-do item not found");
            s.Response(400, "Bad request");
        });
    }

    /// <summary>Ownership through the Activity — see <c>UpdateActivityBacklogProfileEndpoint</c>.</summary>
    protected override Task<bool> AuthorizeAsync(ActivityProjectProfile entity, CancellationToken ct = default) =>
        ownership.OwnsActivityAsync(_dbContext, User.GetId(), entity.ActivityId, ct);

    /// <summary>
    /// The item id is the caller's to type, and the FK proves only that it exists. 404 for "not yours" and
    /// "does not exist" alike, so the route is not an existence oracle over other accounts' items.
    /// </summary>
    protected override async Task<bool> BeforeMapping(ActivityProjectProfile entity,
        PatchActivityProjectProfileShoppingItemRequest req, CancellationToken ct = default)
    {
        if (await ownership.OwnsReferenceAsync(_dbContext, UserOwnedReferenceKeys.TodoListItem, User.GetId(), req.TodoListItemId, ct))
            return true;

        ValidationFailures.Add(new ValidationFailure(nameof(req.TodoListItemId), "To-do item not found."));
        await Send.ErrorsAsync(404, ct);
        return false;
    }

    protected override void Mapping(ActivityProjectProfile entity, PatchActivityProjectProfileShoppingItemRequest req) =>
        entity.ShoppingTodoListItemId = req.TodoListItemId;
}
