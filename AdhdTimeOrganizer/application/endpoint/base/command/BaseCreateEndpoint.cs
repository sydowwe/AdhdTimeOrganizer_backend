using AdhdTimeOrganizer.application.dto.request.@interface;
using AdhdTimeOrganizer.application.endpointGroups;
using AdhdTimeOrganizer.application.extensions;
using AdhdTimeOrganizer.application.helper;
using AdhdTimeOrganizer.application.mapper.@interface;
using AdhdTimeOrganizer.domain.model.entity.user;
using AdhdTimeOrganizer.infrastructure.persistence;
using FastEndpoints;
using Humanizer;

namespace AdhdTimeOrganizer.application.endpoint.@base.command;

public abstract class BaseCreateEndpoint<TEntity, TRequest, TMapper>(
    AppDbContext dbContext,
    TMapper mapper) : Endpoint<TRequest, long>
    where TEntity : class, IEntityWithUser
    where TRequest : class, ICreateRequest
    where TMapper : IBaseCreateMapper<TEntity, TRequest>
{
    public virtual string[] AllowedRoles()
    {
        return EndpointHelper.GetUserOrHigherRoles();
    }

    public virtual string Route => typeof(TEntity).Name.Kebaberize();

    public override void Configure()
    {
        var entityName = typeof(TEntity).Name;
        Post(Route);
        Roles(AllowedRoles());
        Summary(s =>
        {
            s.Summary = $"Create {entityName}";
            s.Description = $"Creates a new {entityName}";
            s.Response<long>(201, "Created");
            s.Response(400, "Bad request");
        });
    }

    public override async Task HandleAsync(TRequest req, CancellationToken ct)
    {
        try
        {
            await BeforeMapping(req, ct);
            var entity = mapper.ToEntity(req, User.GetId());
            await AfterMapping(entity, req, ct);
            await dbContext.Set<TEntity>().AddAsync(entity, ct);
            await dbContext.SaveChangesAsync(ct);

            await Send.ResponseAsync(entity.Id, 201, ct);
        }
        catch (Exception ex)
        {
            var result = DbUtils.HandleException(ex, "Create");
            AddError(result.ErrorMessage!);
            await Send.ErrorsAsync(400, ct);
        }
    }
    protected virtual Task BeforeMapping(TRequest req, CancellationToken ct = default)
    {
        return Task.CompletedTask;
    }

    protected virtual Task AfterMapping(TEntity entity, TRequest req, CancellationToken ct = default)
    {
        return Task.CompletedTask;
    }
}