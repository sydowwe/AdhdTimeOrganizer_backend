using FastEndpoints;
using Humanizer;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.domain.entityInterface;
using Sydowwe.Framework.domain.helper;
using Sydowwe.Framework.infrastructure.persistence;

namespace Sydowwe.Framework.application.endpoint.@base.command;

public abstract class BaseDeleteEndpoint<TEntity>(DbContext dbContext) : EndpointWithoutRequest
    where TEntity : class, IEntityWithId
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
            s.Summary = $"Delete {entityName}";
            s.Description = $"Deletes a {entityName}";
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

            await BeforeDeleteAsync(entity, ct);

            dbContext.Set<TEntity>().Remove(entity);
            await dbContext.SaveChangesAsync(ct);

            await AfterSave(entity, ct);

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
    /// Runs after authorization but <b>before</b> the row is removed — the only place to read state that the
    /// delete (or a cascade off it) is about to destroy, e.g. the ids of dependent rows an out-of-database
    /// system still has to be told about. Pair it with <see cref="AfterSave"/>, which fires once the delete
    /// has actually committed.
    /// </summary>
    protected virtual Task BeforeDeleteAsync(TEntity entity, CancellationToken ct = default) => Task.CompletedTask;

    /// <summary>
    /// Runs after the delete commits. Mirrors the hook on the create/update bases; use it for effects that
    /// must not happen if the delete failed.
    /// </summary>
    protected virtual Task AfterSave(TEntity entity, CancellationToken ct = default) => Task.CompletedTask;
}