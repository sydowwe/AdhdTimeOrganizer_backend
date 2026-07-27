using FastEndpoints;
using Humanizer;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.application.dto.request.@interface;
using Sydowwe.Framework.domain.entityInterface;
using Sydowwe.Framework.domain.helper;
using Sydowwe.Framework.infrastructure.persistence;

namespace Sydowwe.Framework.application.endpoint.@base.command;

public abstract class BasePatchEndpoint<TEntity, TRequest>(
    DbContext dbContext) : Endpoint<TRequest>
    where TEntity : class, IEntityWithId
    where TRequest : class, IPatchRequest
{
    public virtual string[] AllowedRoles() => IEndpoint.GetUserRole();

    public override void Configure()
    {
        var entityName = typeof(TEntity).Name;
        Patch($"/{entityName.Kebaberize()}/{{id:long}}");
        Roles(AllowedRoles());
        Summary(s =>
        {
            s.Summary = $"Patch {entityName}";
            s.Description = $"Partially updates an existing {entityName}";
            s.Response(204, "Success");
            s.Response(404, "Not found");
            s.Response(400, "Bad request");
        });
    }

    public override async Task HandleAsync(TRequest req, CancellationToken ct)
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

            Mapping(entity, req);

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

    protected abstract void Mapping(TEntity entity, TRequest req);

    /// <summary>
    /// Post-fetch ownership/authorization check on the loaded entity (e.g. IDOR / "is this row mine?").
    /// Return <c>false</c> to respond 403. Default allows everyone the role check already let through.
    /// </summary>
    protected virtual Task<bool> AuthorizeAsync(TEntity entity, CancellationToken ct = default) => Task.FromResult(true);
}