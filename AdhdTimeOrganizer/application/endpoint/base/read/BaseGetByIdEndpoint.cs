using AdhdTimeOrganizer.application.dto.request.generic;
using AdhdTimeOrganizer.application.dto.response;
using AdhdTimeOrganizer.application.dto.response.@base;
using AdhdTimeOrganizer.application.helper;
using AdhdTimeOrganizer.domain.model.entityInterface;
using AdhdTimeOrganizer.infrastructure.persistence;
using FastEndpoints;
using Humanizer;
using Microsoft.EntityFrameworkCore;

namespace AdhdTimeOrganizer.application.endpoint.@base.read;

public abstract class BaseGetByIdEndpoint<TEntity, TResponse>(AppDbContext dbContext) : Endpoint<IdRequest, TResponse>
    where TEntity : class, IEntityWithId
    where TResponse : class, IIdResponse, IProjectionResponse<TResponse, TEntity>
{
    public virtual string[] AllowedRoles() => EndpointHelper.GetAdminOrHigherRoles();

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

    protected virtual Task<bool> AuthorizeAsync(TResponse entity, CancellationToken ct) => Task.FromResult(true);

    protected virtual TResponse PostProcess(TResponse entity) => entity;

    public override async Task HandleAsync(IdRequest req, CancellationToken ct)
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
            await Send.ForbiddenAsync(ct);
            return;
        }

        await Send.OkAsync(PostProcess(entity), ct);
    }
}