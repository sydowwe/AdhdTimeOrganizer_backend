using FastEndpoints;
using Humanizer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Sydowwe.Framework.application.dto.response;
using Sydowwe.Framework.application.dto.response.@base;
using Sydowwe.Framework.domain.entityInterface;
using Sydowwe.Framework.domain.helper;

namespace Sydowwe.Framework.application.endpoint.@base.read;

public abstract class BaseGetAllByParentEndpoint<TEntity, TResponse>(DbContext dbContext) : EndpointWithoutRequest<List<TResponse>>
    where TEntity : class, IEntityWithId
    where TResponse : class, IIdResponse, IProjectionResponse<TResponse, TEntity>
{
    public virtual string[] AllowedRoles() => IEndpoint.GetUserRole();

    protected abstract string ParentName { get; }

    public override void Configure()
    {
        var entityName = typeof(TEntity).Name;
        Get($"/{entityName.Kebaberize()}/by-{ParentName.Kebaberize()}/{{parentId:long}}");
        Roles(AllowedRoles());
    }

    protected virtual Task<bool> AuthorizeAsync(long parentId, CancellationToken ct) => Task.FromResult(true);

    protected abstract IQueryable<TEntity> FilterByParent(IQueryable<TEntity> query, long parentId);

    public override async Task HandleAsync(CancellationToken ct)
    {
        try
        {
            var parentId = Route<long>("parentId");

            if (!await AuthorizeAsync(parentId, ct))
            {
                await Send.ForbiddenAsync(ct);
                return;
            }

            var query = FilterByParent(dbContext.Set<TEntity>().AsNoTracking(), parentId);
            await Send.OkAsync(await TResponse.Projection(query).ToListAsync(ct), ct);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error retrieving {Entity} by parent", typeof(TEntity).Name);
            AddError("An internal error occurred.");
            await Send.ErrorsAsync(500, ct);
        }
    }
}