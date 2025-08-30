using AdhdTimeOrganizer.application.dto.request.@interface;
using AdhdTimeOrganizer.application.dto.response.@base;
using AdhdTimeOrganizer.application.endpoint.@base.read;
using AdhdTimeOrganizer.application.mapper.@interface;
using AdhdTimeOrganizer.domain.helper;
using AdhdTimeOrganizer.domain.model.entityInterface;
using AdhdTimeOrganizer.infrastructure.persistence;
using FastEndpoints;
using Humanizer;

namespace AdhdTimeOrganizer.application.endpoint.@base.command;

public abstract class BaseCreateEndpoint<TEntity, TRequest, TResponse, TMapper>(
    AppCommandDbContext dbContext,
    TMapper mapper) : Endpoint<TRequest, TResponse>
    where TEntity : class, IEntityWithId
    where TRequest : class, ICreateRequest
    where TResponse : class, IIdResponse
    where TMapper : IBaseCreateMapper<TEntity, TRequest>, IBaseResponseMapper<TEntity, TResponse>
{
    public virtual string[] AllowedRoles()
    {
        return EndpointHelper.GetUserOrHigherRoles();
    }

    public override void Configure()
    {
        var entityName = typeof(TEntity).Name;
        Post($"/{entityName.Kebaberize()}");
        Roles(AllowedRoles());
        Summary(s =>
        {
            s.Summary = $"Create {entityName}";
            s.Description = $"Creates a new {entityName}";
            s.Response<TResponse>(201, "Created");
            s.Response(400, "Bad request");
        });
    }

    public override async Task HandleAsync(TRequest req, CancellationToken ct)
    {
        try
        {
            var entity = mapper.ToEntity(req);
            await dbContext.Set<TEntity>().AddAsync(entity, ct);
            await dbContext.SaveChangesAsync(ct);

            var response = mapper.ToResponse(entity);
            await SendCreatedAtAsync<BaseGetByIdEndpoint<TEntity, TResponse, TMapper>>(
                new { id = response.Id },
                response,
                cancellation: ct);
        }
        catch (Exception ex)
        {
            var result = DbUtils.HandleException(ex, nameof(HandleAsync));
            AddError(result.ErrorMessage!);
            await SendErrorsAsync(400, ct);
        }
    }
}