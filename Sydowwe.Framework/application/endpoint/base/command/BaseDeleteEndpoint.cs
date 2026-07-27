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
    public virtual string[] AllowedRoles() => IEndpoint.GetUserRole();

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

            dbContext.Set<TEntity>().Remove(entity);
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
}