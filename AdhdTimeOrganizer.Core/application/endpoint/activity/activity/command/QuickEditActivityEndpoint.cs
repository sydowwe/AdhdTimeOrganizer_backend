using AdhdTimeOrganizer.Core.application.dto.request.activity;
using AdhdTimeOrganizer.Core.application.validator;
using AdhdTimeOrganizer.Core.domain.model.entity.activity;
using AdhdTimeOrganizer.Core.infrastructure.persistence.extensions;
using AdhdTimeOrganizer.Core.infrastructure.persistence;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.application.error;
using Sydowwe.Framework.domain.helper;
using Sydowwe.Framework.infrastructure.persistence;

namespace AdhdTimeOrganizer.Core.application.endpoint.activity.activity.command;

public class QuickEditActivityEndpoint(DbContext dbContext) : Endpoint<QuickEditActivityRequest>
{
    protected virtual string[] AllowedRoles() => this.GetDefaultRoles();

    public override void Configure()
    {
        Put("/activity/{id:long:required}/quick-edit");
        Roles(AllowedRoles());
        Validator<QuickEditActivityValidator>();

        Summary(s =>
        {
            s.Summary = "Quick edit activity";
            s.Description = "Overwrites editable fields of an existing activity";
            s.Response(204, "Updated");
            s.Response(404, "Not found");
            s.Response(400, "Bad request");
        });
    }

    public override async Task HandleAsync(QuickEditActivityRequest req, CancellationToken ct)
    {
        try
        {
            // Lifts AppQueryFilters.ActivityArchived and nothing else, for the same reason
            // UpdateActivityEndpoint does: the archive view offers this row action, and
            // QuickEditActivityRequest cannot carry IsArchived, so the edit cannot move the flag. A
            // keyed FirstOrDefault rather than FindAsync, which cannot lift a filter. The per-user
            // filter stays on, so another user's id is simply not found — 404, never 403.
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

            // QuickEditActivityValidator checks the category id's magnitude only, and another user's
            // category satisfies the FK constraint — so this is the ownership check. 404, not 403, to
            // keep the id unconfirmable across accounts (see MergeActivityEndpoint).
            if (!await dbContext.AllVisibleAsync<ActivityCategory>(ct, req.CategoryId))
            {
                AddError("Activity category not found.");
                await Send.ErrorsAsync(404, ct);
                return;
            }

            entity.Name = req.Name;
            entity.Text = req.Text;
            entity.CategoryId = req.CategoryId;

            var result = await dbContext.UpdateEntityAsync(entity, ct);
            if (result.Failed)
            {
                AddError(result.ErrorMessage!);
                await Send.ErrorsAsync(this.ErrorStatus(result.ErrorType), ct);
                return;
            }

            await Send.NoContentAsync(ct);
        }
        catch (Exception ex)
        {
            var result = DbUtils.HandleException(ex, nameof(HandleAsync));
            AddError(result.ErrorMessage!);
            await Send.ErrorsAsync(this.ErrorStatus(result.ErrorType), ct);
        }
    }
}