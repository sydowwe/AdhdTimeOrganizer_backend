using AdhdTimeOrganizer.application.helper;
using AdhdTimeOrganizer.domain.model.entityInterface;
using AdhdTimeOrganizer.infrastructure.persistence;
using FastEndpoints;
using Humanizer;

namespace AdhdTimeOrganizer.application.endpoint.@base.command;

public abstract class BaseSoftDeleteEndpoint<TEntity>(AppDbContext dbContext) : EndpointWithoutRequest
    where TEntity : class, IEntityWithId, ISoftDeletable
{
    public virtual string[] AllowedRoles() => EndpointHelper.GetAdminOrHigherRoles();

    public virtual string Route => typeof(TEntity).Name.Kebaberize();

    public override void Configure()
    {
        var entityName = typeof(TEntity).Name;
        Delete(Route + "/{id:long}");
        Roles(AllowedRoles());
        Summary(s =>
        {
            s.Summary = $"Soft-delete {entityName}";
            s.Description = $"Sets IsActive = false on {entityName}. Does not remove the record.";
            s.Response(204, "Success");
            s.Response(404, "Not found");
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var entity = await dbContext.Set<TEntity>().FindAsync([Route<long>("id")], ct);
        if (entity == null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        if (!entity.IsActive)
        {
            await Send.NoContentAsync(ct);
            return;
        }

        try
        {
            entity.IsActive = false;
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
}