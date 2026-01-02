using System.Linq.Expressions;
using AdhdTimeOrganizer.application.dto.request.generic;
using AdhdTimeOrganizer.application.dto.response.@base;
using AdhdTimeOrganizer.application.extensions;
using AdhdTimeOrganizer.application.mapper.@interface;
using AdhdTimeOrganizer.domain.helper;
using AdhdTimeOrganizer.domain.model.entity.user;
using AdhdTimeOrganizer.domain.model.entityInterface;
using AdhdTimeOrganizer.infrastructure.extension;
using AdhdTimeOrganizer.infrastructure.persistence;
using FastEndpoints;
using Humanizer;
using Microsoft.EntityFrameworkCore;

namespace AdhdTimeOrganizer.application.endpoint.@base.read;

public abstract class BaseGetByFieldEndpoint<TEntity, TResponse, TMapper>(AppCommandDbContext dbContext, TMapper mapper) : EndpointWithoutRequest<TResponse>
    where TEntity : class, IEntityWithUser
    where TResponse : class, IIdResponse
    where TMapper : IBaseResponseMapper<TEntity, TResponse>
{
    private readonly TMapper _mapper = mapper;

    public virtual string[] AllowedRoles()
    {
        return EndpointHelper.GetUserOrHigherRoles();
    }

    public virtual bool FilteredByUser => true;
    protected abstract string FieldName { get; }

    public override void Configure()
    {
        var entityName = typeof(TEntity).Name;
        Get($"/{entityName.Kebaberize()}/by-{FieldName}/{{value}}");
        Roles(AllowedRoles());
        Summary(s =>
        {
            s.Summary = $"Get {entityName} by Name";
            s.Description = $"Retrieves a specific {entityName} by its name";
            s.Response<TResponse>(200, "Success");
            s.Response(404, "Not found");
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var value = Route<string>("value");
        if (string.IsNullOrEmpty(value))
        {
            AddError($"The value parameter is required and cannot be empty. /{typeof(TEntity).Name.Kebaberize()}/by-{FieldName}/{{value}}");
            await SendErrorsAsync(cancellation: ct);
            return;
        }

        var dbSet = dbContext.Set<TEntity>();
        var query = WithIncludes(dbSet);

        if (FilteredByUser)
        {
            query = query.FilteredByUser(User.GetId());
        }

        var entity = await query.FirstOrDefaultAsync(FilterQuery(value), ct);
        if (entity == null)
        {
            await SendNotFoundAsync(ct);
            return;
        }

        var response = _mapper.ToResponse(entity);
        await SendOkAsync(response, ct);
    }

    protected virtual IQueryable<TEntity> WithIncludes(IQueryable<TEntity> query) => query;

    protected abstract Expression<Func<TEntity, bool>> FilterQuery(string value);
}