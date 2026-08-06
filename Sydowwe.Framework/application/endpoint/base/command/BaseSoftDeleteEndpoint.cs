using FastEndpoints;
using Humanizer;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.domain.entityInterface;
using Sydowwe.Framework.domain.helper;
using Sydowwe.Framework.infrastructure.persistence;

namespace Sydowwe.Framework.application.endpoint.@base.command;

public abstract class BaseSoftDeleteEndpoint<TEntity>(DbContext dbContext) : EndpointWithoutRequest
    where TEntity : class, IEntityWithId, ISoftDeletable
{
    protected virtual string[] AllowedRoles() => this.GetDefaultRoles();

    public virtual string Route => typeof(TEntity).Name.Kebaberize();

    public override void Configure()
    {
        var entityName = typeof(TEntity).Name;
        Delete(Route + "/{id:long}");
        Roles(AllowedRoles());
        Summary(s =>
        {
            s.Summary = $"Soft-delete {entityName}";
            s.Description = $"Sets IsActive = false on {entityName}. Does not remove the record.";
            s.Response(204, "Success");
            s.Response(404, "Not found");
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        try
        {
            var entity = await dbContext.Set<TEntity>().FindAsync([Route<long>("id")], ct);
            if (entity == null)
            {
                await Send.NotFoundAsync(ct);
                return;
            }

            if (!await AuthorizeAsync(entity, ct))
            {
                await Send.ForbiddenAsync(ct);
                return;
            }

            if (!entity.IsActive)
            {
                await Send.NoContentAsync(ct);
                return;
            }

            if (!await BeforeDeleteAsync(entity, ct))
                return;

            entity.IsActive = false;
            dbContext.Set<TEntity>().Update(entity);
            await dbContext.SaveChangesAsync(ct);
            await Send.NoContentAsync(ct);
        }
        catch (Exception ex)
        {
            var result = DbUtils.HandleException(ex, nameof(HandleAsync));
            AddError(result.ErrorMessage!);
            await Send.ErrorsAsync(EndpointHelper.ToStatusCode(result.ErrorType), ct);
        }
    }

    /// <summary>
    /// Post-fetch ownership/authorization check on the loaded entity (e.g. IDOR / "is this row mine?").
    /// Return <c>false</c> to respond 403. Default allows everyone the role check already let through.
    /// </summary>
    protected virtual Task<bool> AuthorizeAsync(TEntity entity, CancellationToken ct = default) => Task.FromResult(true);

    /// <summary>
    /// Domain guard run after the entity is loaded, authorized and confirmed still active, but before it is
    /// soft-deleted. Return <c>true</c> to proceed. Return <c>false</c> to abort — the hook is then responsible
    /// for sending its own response (e.g. <c>AddError(...)</c> + <c>Send.ErrorsAsync(409, ct)</c>). Default
    /// allows the delete.
    /// </summary>
    protected virtual Task<bool> BeforeDeleteAsync(TEntity entity, CancellationToken ct = default) => Task.FromResult(true);
}