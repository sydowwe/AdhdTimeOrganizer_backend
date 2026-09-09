using AdhdTimeOrganizer.Core.application.dto.request.activity;
using AdhdTimeOrganizer.Core.application.validator;
using AdhdTimeOrganizer.Core.domain.model.entity.activity;
using AdhdTimeOrganizer.Core.infrastructure.persistence.extensions;
using AdhdTimeOrganizer.Core.infrastructure.persistence;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.domain.helper;
using Sydowwe.Framework.infrastructure.persistence;

namespace AdhdTimeOrganizer.Core.application.endpoint.activity.activity.command;

public class CloneActivityEndpoint(DbContext dbContext) : Endpoint<QuickEditActivityRequest, long?>
{
    protected virtual string[] AllowedRoles() => this.GetDefaultRoles();

    public override void Configure()
    {
        Post("/activity/{id:long:required}/clone");
        Roles(AllowedRoles());
        Validator<QuickEditActivityValidator>();
        Summary(s =>
        {
            s.Summary = "Clone activity";
            s.Description = "Creates a new activity cloned from an existing one";
            s.Response(200, "Cloned successfully");
            s.Response(404, "Not found");
            s.Response(400, "Bad request");
        });
    }

    public override async Task HandleAsync(QuickEditActivityRequest req, CancellationToken ct)
    {
        try
        {
            // Lifts AppQueryFilters.ActivityArchived and nothing else: the archive view offers this row
            // action, and cloning reads the source rather than writing it, so archival cannot leak into
            // the request. A keyed FirstOrDefault rather than FindAsync, which cannot lift a filter. The
            // per-user filter stays on, so another user's id is simply not found — 404, never 403.
            var id = Route<long>("id");
            var entity = await dbContext.Set<Activity>()
                .IgnoreQueryFilters([AppQueryFilters.ActivityArchived])
                .FirstOrDefaultAsync(a => a.Id == id, ct);
            if (entity == null)
            {
                AddError("Activity not found.");
                await Send.ErrorsAsync(404, ct);
                return;
            }

            // Same ownership check as QuickEditActivityEndpoint — the clone form is the quick-edit form
            // and carries the same caller-supplied category id.
            if (!await dbContext.AllVisibleAsync<ActivityCategory>(ct, req.CategoryId))
            {
                AddError("Activity category not found.");
                await Send.ErrorsAsync(404, ct);
                return;
            }

            var clonedEntity = entity.Clone();
            // Clone() is a MemberwiseClone, so it carries the source's IsArchived. Now that an archived
            // source is reachable, the copy has to start active — a clone the user cannot see anywhere
            // but the archive view is not what the row action promises. Archiving it again is one call.
            clonedEntity.IsArchived = false;
            clonedEntity.Name = clonedEntity.Name == req.Name ? $"{req.Name} (copy)" : req.Name;
            clonedEntity.Text = req.Text;
            clonedEntity.CategoryId = req.CategoryId;

            var result = await dbContext.AddEntityAsync(clonedEntity, ct);
            if (result.Failed)
            {
                AddError(result.ErrorMessage!);
                await Send.ErrorsAsync(400, ct);
                return;
            }

            await Send.OkAsync(clonedEntity.Id, ct);
        }
        catch (Exception ex)
        {
            var result = DbUtils.HandleException(ex, nameof(HandleAsync));
            AddError(result.ErrorMessage!);
            await Send.ErrorsAsync(400, ct);
        }
    }
}