using AdhdTimeOrganizer.application.dto.request.generic;
using AdhdTimeOrganizer.application.dto.response.@base;
using AdhdTimeOrganizer.application.mapper.@interface;
using AdhdTimeOrganizer.domain.helper;
using AdhdTimeOrganizer.domain.model.entityInterface;
using AdhdTimeOrganizer.infrastructure.persistence;
using FastEndpoints;
using Humanizer;
using Microsoft.EntityFrameworkCore;

namespace AdhdTimeOrganizer.application.endpoint.@base.read;

public abstract class BaseGetByIdEndpoint<TEntity, TResponse, TMapper>(AppCommandDbContext dbContext, TMapper mapper) : Endpoint<IdRequest, TResponse>
    where TEntity : class, IEntityWithId
    where TResponse : class, IIdResponse
    where TMapper : IBaseResponseMapper<TEntity, TResponse>
{
    private readonly TMapper _mapper = mapper;

    public virtual string[] AllowedRoles()
    {
        return EndpointHelper.GetUserOrHigherRoles();
    }

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

    public override async Task HandleAsync(IdRequest req, CancellationToken ct)
    {
        var dbSet = dbContext.Set<TEntity>();
        var query = WithIncludes(dbSet);

        var entity = await query.FirstOrDefaultAsync(e => e.Id == req.Id, ct);
        if (entity == null)
        {
            await SendNotFoundAsync(ct);
            return;
        }

        var response = _mapper.ToResponse(entity);
        await SendOkAsync(response, ct);
    }

    protected virtual IQueryable<TEntity> WithIncludes(IQueryable<TEntity> query) => query;
}