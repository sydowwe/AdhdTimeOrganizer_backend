using FastEndpoints;
using Humanizer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Sydowwe.Framework.application.dto.request.generic;
using Sydowwe.Framework.application.dto.response;
using Sydowwe.Framework.application.dto.response.@base;
using Sydowwe.Framework.domain.entityInterface;
using Sydowwe.Framework.domain.helper;

namespace Sydowwe.Framework.application.endpoint.@base.read;

public abstract class BaseGetByIdEndpoint<TEntity, TResponse>(DbContext dbContext) : Endpoint<IdRequest, TResponse>
    where TEntity : class, IEntityWithId
    where TResponse : class, IIdResponse, IProjectionResponse<TResponse, TEntity>
{
    public virtual string[] AllowedRoles() => IEndpoint.GetUserRole();

    public override void Configure()
    {
        var entityName = typeof(TEntity).Name;
        Get($"/{entityName.Kebaberize()}/{{id}}");
        Roles(AllowedRoles());
        Summary(s =>
        {
            s.Summary = $"Get {entityName} by ID";
            s.Description = $"Retrieves a specific {entityName} by its ID";
            s.Response<TResponse>(200, "Success");
            s.Response(404, "Not found");
        });
    }

    protected abstract Task<bool> AuthorizeAsync(TResponse entity, CancellationToken ct);

    /// <summary>
    /// What a failed <see cref="AuthorizeAsync"/> returns. Default 403 (the caller may know the row exists).
    /// Override to <c>true</c> on endpoints where mere existence is confidential — a 403 there tells an
    /// unauthorized caller that the id is real, so those must answer 404 exactly like a missing row.
    /// </summary>
    protected virtual bool NotFoundWhenUnauthorized => false;

    protected virtual TResponse PostProcess(TResponse entity) => entity;

    /// <summary>
    /// Async post-process hook, run after <see cref="AuthorizeAsync"/>. Default delegates to the synchronous
    /// <see cref="PostProcess"/>. Override when the post-processing needs an awaitable call — e.g. overlaying a
    /// value resolved from another module through a Kernel query.
    /// </summary>
    protected virtual Task<TResponse> PostProcessAsync(TResponse entity, CancellationToken ct) => Task.FromResult(PostProcess(entity));

    public override async Task HandleAsync(IdRequest req, CancellationToken ct)
    {
        try
        {
            var dbSet = dbContext.Set<TEntity>();

            var entity = await TResponse.Projection(dbSet.AsNoTracking().Where(e => e.Id == req.Id)).FirstOrDefaultAsync(ct);
            if (entity == null)
            {
                await Send.NotFoundAsync(ct);
                return;
            }

            if (!await AuthorizeAsync(entity, ct))
            {
                if (NotFoundWhenUnauthorized)
                    await Send.NotFoundAsync(ct);
                else
                    await Send.ForbiddenAsync(ct);
                return;
            }

            await Send.OkAsync(await PostProcessAsync(entity, ct), ct);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error retrieving {Entity} by id", typeof(TEntity).Name);
            AddError("An internal error occurred.");
            await Send.ErrorsAsync(500, ct);
        }
    }
}