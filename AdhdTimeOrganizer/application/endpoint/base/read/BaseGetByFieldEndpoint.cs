using AdhdTimeOrganizer.application.dto.response;
using AdhdTimeOrganizer.application.dto.response.@base;
using AdhdTimeOrganizer.application.helper;
using AdhdTimeOrganizer.domain.model.entityInterface;
using AdhdTimeOrganizer.infrastructure.persistence;
using FastEndpoints;
using Humanizer;
using Microsoft.EntityFrameworkCore;

namespace AdhdTimeOrganizer.application.endpoint.@base.read;

public abstract class BaseGetByFieldEndpoint<TEntity, TResponse>(AppDbContext dbContext) : EndpointWithoutRequest<TResponse>
    where TEntity : class, IEntityWithId
    where TResponse : class, IIdResponse, IProjectionResponse<TResponse, TEntity>
{
    public virtual string[] AllowedRoles() => EndpointHelper.GetAdminOrHigherRoles();

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
            await Send.ErrorsAsync(cancellation: ct);
            return;
        }

        var query = dbContext.Set<TEntity>();

        var entity = await TResponse.Projection(FilterByField(query.AsNoTracking(), value)).FirstOrDefaultAsync(ct);
        if (entity == null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        await Send.OkAsync(entity, ct);
    }

    protected abstract IQueryable<TEntity> FilterByField(IQueryable<TEntity> query, string value);
}