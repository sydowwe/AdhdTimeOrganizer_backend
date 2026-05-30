using AdhdTimeOrganizer.application.dto.request.@interface;
using AdhdTimeOrganizer.application.helper;
using AdhdTimeOrganizer.domain.model.entityInterface;
using AdhdTimeOrganizer.infrastructure.persistence;
using FastEndpoints;
using Humanizer;

namespace AdhdTimeOrganizer.application.endpoint.@base.command;

public abstract class BaseCreateEndpoint<TEntity, TRequest>(AppDbContext dbContext) : Endpoint<TRequest, long>
    where TEntity : class, IEntityWithId
    where TRequest : class, ICreateRequest<TEntity>
{
    public virtual string[] AllowedRoles() => EndpointHelper.GetAdminOrHigherRoles();

    public virtual string Route => $"/{typeof(TEntity).Name.Kebaberize()}";

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
            if (!await BeforeMapping(req, ct)) return;

            var entity = req.ToEntity;

            if (!await AfterMapping(entity, req, ct)) return;

            await dbContext.Set<TEntity>().AddAsync(entity, ct);
            await dbContext.SaveChangesAsync(ct);

            await AfterSave(entity, ct);

            await Send.ResponseAsync(entity.Id, 201, ct);
        }
        catch (Exception ex)
        {
            var result = DbUtils.HandleException(ex, "Create");
            AddError(result.ErrorMessage!);
            await Send.ErrorsAsync(EndpointHelper.ToStatusCode(result.ErrorType), ct);
        }
    }

    protected virtual Task<bool> BeforeMapping(TRequest req, CancellationToken ct = default)
        => Task.FromResult(true);

    protected virtual Task<bool> AfterMapping(TEntity entity, TRequest req, CancellationToken ct = default)
        => Task.FromResult(true);

    protected virtual Task AfterSave(TEntity entity, CancellationToken ct = default) => Task.CompletedTask;
}